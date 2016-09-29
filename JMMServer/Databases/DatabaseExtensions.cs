﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using JMMServer.Entities;
using JMMServer.Repositories;
using NHibernate;
using NLog;
using System.Globalization;
using System.IO;
using System.Windows;
using JMMServer.Repositories.Cached;
using JMMServer.Repositories.Direct;
using NHibernate.Util;

namespace JMMServer.Databases
{
    public static class DatabaseExtensions
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static IDatabase Instance
        {
            get
            {
                if (ServerSettings.DatabaseType.Trim()
                    .Equals(Constants.DatabaseType.SqlServer, StringComparison.InvariantCultureIgnoreCase))
                    return SQLServer.Instance;
                if (ServerSettings.DatabaseType.Trim()
                    .Equals(Constants.DatabaseType.Sqlite, StringComparison.InvariantCultureIgnoreCase))
                    return SQLite.Instance;
                return MySQL.Instance;
            }
        }

        public static string GetDatabaseBackupName(int version)
        {
            //TODO this need to be fixed if we want to remove JMMServer Administration dependency, 
            // all the storage should be outside program files.
            string backupath = ServerSettings.DatabaseBackupDirectory;
            try
            {
                Directory.CreateDirectory(backupath);
            }
            catch
            {
            }
            string fname = ServerSettings.DatabaseName + "_" + version.ToString("D3") + "_" +
                           DateTime.Now.Year.ToString("D4") + DateTime.Now.Month.ToString("D2") +
                           DateTime.Now.Day.ToString("D2") + DateTime.Now.Hour.ToString("D2") +
                           DateTime.Now.Minute.ToString("D2");
            return Path.Combine(backupath, fname);
        }

        internal static void AddVersion(this IDatabase db, string version, string revision, string command)
        {
            Versions v = new Versions();
            v.VersionType = Constants.DatabaseTypeKey;
            v.VersionValue = version.ToString();
            v.VersionRevision = revision.ToString();
            v.VersionCommand = command;
            v.VersionProgram = ServerState.Instance.ApplicationVersion;
            RepoFactory.Versions.Save(v);
            Dictionary<string, Versions> dv = new Dictionary<string, Versions>();
            if (db.AllVersions.ContainsKey(v.VersionValue))
                dv = db.AllVersions[v.VersionValue];
            else
                db.AllVersions.Add(v.VersionValue, dv);
            dv.Add(v.VersionRevision, v);
            db.AllVersions.Add(v.VersionValue, new Dictionary<string, Versions>());
        }

        public static void ExecuteWithException(this IDatabase db, DatabaseCommand cmd)
        {
            Tuple<bool, string> t = db.ExecuteCommand(cmd);
            if (!t.Item1)
                throw new DatabaseCommandException(t.Item2, cmd);
        }

        public static void ExecuteWithException(this IDatabase db, IEnumerable<DatabaseCommand> cmds)
        {
            cmds.ForEach(a => db.ExecuteWithException(a));
        }

        public static int GetDatabaseVersion(this IDatabase db)
        {
            if (db.AllVersions.Count == 0)
                return 0;
            return Int32.Parse(db.AllVersions.Keys.ToList().Max());
        }

        internal static void PreFillVersions(this IDatabase db, IEnumerable<DatabaseCommand> commands)
        {
            if (db.AllVersions.Count == 1 && db.AllVersions.Values.ElementAt(0).Count == 1)
            {
                Versions v = db.AllVersions.Values.ElementAt(0).Values.ElementAt(0);
                string value = v.VersionValue;
                db.AllVersions.Clear();
                RepoFactory.Versions.Delete(v);
                foreach (DatabaseCommand dc in commands)
                {
                    if (dc.Version <= int.Parse(value))
                    {
                        db.AddVersion(dc.Version.ToString(), dc.Revision.ToString(), dc.CommandName);
                    }
                }
            }
        }

        internal static Tuple<bool, string> ExecuteCommand(this IDatabase db, DatabaseCommand cmd,
            Func<string, Tuple<bool, string>> commandWrapper)
        {

            if (cmd.Version != 0 && cmd.Revision != 0)
            {
                if (db.AllVersions.ContainsKey(cmd.Version.ToString()) &&
                    db.AllVersions[cmd.Version.ToString()].ContainsKey(cmd.Revision.ToString()))
                    return new Tuple<bool, string>(true, null);
            }
            Tuple<bool, string> ret;

            switch (cmd.Type)
            {
                case DatabaseCommandType.CodedCommand:
                    ret = cmd.UpdateCommand();
                    break;
                case DatabaseCommandType.PostDatabaseFix:
                    try
                    {
                        DatabaseFixes.AddFix(db, cmd);
                        ret = new Tuple<bool, string>(true, null);
                    }
                    catch (Exception e)
                    {
                        ret = new Tuple<bool, string>(false, e.ToString());
                    }
                    break;
                default:
                    ret = commandWrapper(cmd.Command);
                    break;
            }
            if (cmd.Version != 0 && ret.Item1)
            {
                db.AddVersion(cmd.Version.ToString(), cmd.Revision.ToString(), cmd.CommandName);
            }
            return ret;
        }

        public static bool InitDB(this IDatabase database)
        {
            try
            {
                DatabaseFixes.InitFixes();
                if (!database.DatabaseAlreadyExists())
                {
                    database.CreateDatabase();
                    Thread.Sleep(3000);
                }
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

                JMMService.CloseSessionFactory();
                ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_Initializing;
                ISessionFactory temp = JMMService.SessionFactory;
                int version = database.GetDatabaseVersion();
                if (version > database.RequiredVersion)
                {
                    ServerState.Instance.CurrentSetupStatus =
                        JMMServer.Properties.Resources.Database_NotSupportedVersion;
                    return false;
                }
                if (version < database.RequiredVersion)
                {
                    ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_Backup;
                    database.BackupDatabase(GetDatabaseBackupName(version));
                }
                ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_ApplySchema;
                try
                {
                    database.CreateAndUpdateSchema();
                    RepoFactory.Init();
                    DatabaseFixes.ExecuteDatabaseFixes();
                    database.PopulateInitialData();
                }
                catch (DatabaseCommandException e)
                {
                    logger.Error(e,e.ToString());
                    MessageBox.Show(
                        "Database Error :\n\r " + e.ToString() +
                        "\n\rNotify developers about this error, it will be logged in your logs", "Database Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Server_DatabaseFail;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.Error( ex,"Could not init database: " + ex.ToString());
                ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Server_DatabaseFail;
                return false;
            }
        }

       
        private static void PopulateInitialData(this IDatabase db)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

            ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_Users;
            CreateInitialUsers();

            ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_Filters;
            CreateInitialGroupFilters();

            ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_LockFilters;
            db.CreateOrVerifyLockedFilters();


            ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_RenameScripts;
            CreateInitialRenameScript();

            ServerState.Instance.CurrentSetupStatus = JMMServer.Properties.Resources.Database_CustomTags;
            db.CreateInitialCustomTags();
        }

        public static void CreateOrVerifyLockedFilters(this IDatabase db)
        {
            RepoFactory.GroupFilter.CreateOrVerifyLockedFilters();
        }
        private static void CreateInitialGroupFilters()
        {
            // group filters

            if (RepoFactory.GroupFilter.GetAll().Count() > 0) return;

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

            // Favorites
            GroupFilter gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_Favorites;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            GroupFilterCondition gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.Favourite;
            gfc.ConditionOperator = (int)GroupFilterOperator.Include;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);

            // Missing Episodes
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_MissingEpisodes;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.MissingEpisodesCollecting;
            gfc.ConditionOperator = (int)GroupFilterOperator.Include;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);


            // Newly Added Series
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_Added;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.SeriesCreatedDate;
            gfc.ConditionOperator = (int)GroupFilterOperator.LastXDays;
            gfc.ConditionParameter = "10";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);

            // Newly Airing Series
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_Airing;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.AirDate;
            gfc.ConditionOperator = (int)GroupFilterOperator.LastXDays;
            gfc.ConditionParameter = "30";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);

            // Votes Needed
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_Votes;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.CompletedSeries;
            gfc.ConditionOperator = (int)GroupFilterOperator.Include;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.HasUnwatchedEpisodes;
            gfc.ConditionOperator = (int)GroupFilterOperator.Exclude;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.UserVotedAny;
            gfc.ConditionOperator = (int)GroupFilterOperator.Exclude;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);

            // Recently Watched
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_RecentlyWatched;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.EpisodeWatchedDate;
            gfc.ConditionOperator = (int)GroupFilterOperator.LastXDays;
            gfc.ConditionParameter = "10";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);

            // TvDB/MovieDB Link Missing
            gf = new GroupFilter();
            gf.GroupFilterName = JMMServer.Properties.Resources.Filter_LinkMissing;
            gf.ApplyToSeries = 0;
            gf.BaseCondition = 1;
            gf.Locked = 0;
            gf.FilterType = (int)GroupFilterType.UserDefined;
            gfc = new GroupFilterCondition();
            gfc.ConditionType = (int)GroupFilterConditionType.AssignedTvDBOrMovieDBInfo;
            gfc.ConditionOperator = (int)GroupFilterOperator.Exclude;
            gfc.ConditionParameter = "";
            gf.Conditions.Add(gfc);
            gf.EvaluateAnimeGroups();
            gf.EvaluateAnimeSeries();
            RepoFactory.GroupFilter.Save(gf);
        }

        private static void CreateInitialUsers()
        {

            if (RepoFactory.JMMUser.GetAll().Count() > 0) return;

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

            JMMUser defaultUser = new JMMUser();
            defaultUser.CanEditServerSettings = 1;
            defaultUser.HideCategories = "";
            defaultUser.IsAdmin = 1;
            defaultUser.IsAniDBUser = 1;
            defaultUser.IsTraktUser = 1;
            defaultUser.Password = "";
            defaultUser.Username = JMMServer.Properties.Resources.Users_Default;
            RepoFactory.JMMUser.Save(defaultUser, true);

            JMMUser familyUser = new JMMUser();
            familyUser.CanEditServerSettings = 1;
            familyUser.HideCategories = "ecchi,nudity,sex,sexual abuse,horror,erotic game,incest,18 restricted";
            familyUser.IsAdmin = 1;
            familyUser.IsAniDBUser = 1;
            familyUser.IsTraktUser = 1;
            familyUser.Password = "";
            familyUser.Username = JMMServer.Properties.Resources.Users_FamilyFriendly;
            RepoFactory.JMMUser.Save(familyUser, true);
        }

        private static void CreateInitialRenameScript()
        {
            if (RepoFactory.RenameScript.GetAll().Count() > 0) return;

            RenameScript initialScript = new RenameScript();

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

            initialScript.ScriptName = JMMServer.Properties.Resources.Rename_Default;
            initialScript.IsEnabledOnImport = 0;
            initialScript.Script =
                "// Sample Output: [Coalgirls]_Highschool_of_the_Dead_-_01_(1920x1080_Blu-ray_H264)_[90CC6DC1].mkv" +
                Environment.NewLine +
                "// Sub group name" + Environment.NewLine +
                "DO ADD '[%grp] '" + Environment.NewLine +
                "// Anime Name, use english name if it exists, otherwise use the Romaji name" + Environment.NewLine +
                "IF I(eng) DO ADD '%eng '" + Environment.NewLine +
                "IF I(ann);I(!eng) DO ADD '%ann '" + Environment.NewLine +
                "// Episode Number, don't use episode number for movies" + Environment.NewLine +
                "IF T(!Movie) DO ADD '- %enr'" + Environment.NewLine +
                "// If the file version is v2 or higher add it here" + Environment.NewLine +
                "IF F(!1) DO ADD 'v%ver'" + Environment.NewLine +
                "// Video Resolution" + Environment.NewLine +
                "DO ADD ' (%res'" + Environment.NewLine +
                "// Video Source (only if blu-ray or DVD)" + Environment.NewLine +
                "IF R(DVD),R(Blu-ray) DO ADD ' %src'" + Environment.NewLine +
                "// Video Codec" + Environment.NewLine +
                "DO ADD ' %vid'" + Environment.NewLine +
                "// Video Bit Depth (only if 10bit)" + Environment.NewLine +
                "IF Z(10) DO ADD ' %bitbit'" + Environment.NewLine +
                "DO ADD ') '" + Environment.NewLine +
                "DO ADD '[%CRC]'" + Environment.NewLine +
                "" + Environment.NewLine +
                "// Replacement rules (cleanup)" + Environment.NewLine +
                "DO REPLACE ' ' '_' // replace spaces with underscores" + Environment.NewLine +
                "DO REPLACE 'H264/AVC' 'H264'" + Environment.NewLine +
                "DO REPLACE '0x0' ''" + Environment.NewLine +
                "DO REPLACE '__' '_'" + Environment.NewLine +
                "DO REPLACE '__' '_'" + Environment.NewLine +
                "" + Environment.NewLine +
                "// Replace all illegal file name characters" + Environment.NewLine +
                "DO REPLACE '<' '('" + Environment.NewLine +
                "DO REPLACE '>' ')'" + Environment.NewLine +
                "DO REPLACE ':' '-'" + Environment.NewLine +
                "DO REPLACE '" + ((Char)34).ToString() + "' '`'" + Environment.NewLine +
                "DO REPLACE '/' '_'" + Environment.NewLine +
                "DO REPLACE '/' '_'" + Environment.NewLine +
                "DO REPLACE '\\' '_'" + Environment.NewLine +
                "DO REPLACE '|' '_'" + Environment.NewLine +
                "DO REPLACE '?' '_'" + Environment.NewLine +
                "DO REPLACE '*' '_'" + Environment.NewLine;

            RepoFactory.RenameScript.Save(initialScript);
        }

        /*
		private static void CreateContinueWatchingGroupFilter()
		{
			// group filters
			GroupFilterRepository repFilters = new GroupFilterRepository();
			GroupFilterConditionRepository repGFC = new GroupFilterConditionRepository();

			using (var session = JMMService.SessionFactory.OpenSession())
			{
				// check if it already exists
				List<GroupFilter> lockedGFs = repFilters.GetLockedGroupFilters(session);

				if (lockedGFs != null)
				{
                    // if it already exists we can leave
                    foreach (GroupFilter gfTemp in lockedGFs)
                    {
                        if (gfTemp.FilterType == (int)GroupFilterType.ContinueWatching)
                            return;
                    }

                    // the default value when the column was added to the database was '1'
                    // this is only needed for users of a migrated database
                    foreach (GroupFilter gfTemp in lockedGFs)
                    {
                        if (gfTemp.GroupFilterName.Equals(Constants.GroupFilterName.ContinueWatching, StringComparison.InvariantCultureIgnoreCase) &&
                            gfTemp.FilterType != (int)GroupFilterType.ContinueWatching)
                        {
	                        DatabaseFixes.FixContinueWatchingGroupFilter_20160406();
                            return;
                        }
                    }
				}

				GroupFilter gf = new GroupFilter();
				gf.GroupFilterName = Constants.GroupFilterName.ContinueWatching;
				gf.Locked = 1;
				gf.SortingCriteria = "4;2"; // by last watched episode desc
				gf.ApplyToSeries = 0;
				gf.BaseCondition = 1; // all
                gf.FilterType = (int)GroupFilterType.ContinueWatching;

                repFilters.Save(gf,true,null);

				GroupFilterCondition gfc = new GroupFilterCondition();
				gfc.ConditionType = (int)GroupFilterConditionType.HasWatchedEpisodes;
				gfc.ConditionOperator = (int)GroupFilterOperator.Include;
				gfc.ConditionParameter = "";
				gfc.GroupFilterID = gf.GroupFilterID;
				repGFC.Save(gfc);

				gfc = new GroupFilterCondition();
				gfc.ConditionType = (int)GroupFilterConditionType.HasUnwatchedEpisodes;
				gfc.ConditionOperator = (int)GroupFilterOperator.Include;
				gfc.ConditionParameter = "";
				gfc.GroupFilterID = gf.GroupFilterID;
				repGFC.Save(gfc);
			}
		}
		*/

        public static void CreateInitialCustomTags(this IDatabase db)
        {
            try
            {
                // group filters

                if (RepoFactory.CustomTag.GetAll().Count() > 0) return;

                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ServerSettings.Culture);

                // Dropped
                CustomTag tag = new CustomTag();
                tag.TagName = JMMServer.Properties.Resources.CustomTag_Dropped;
                tag.TagDescription = JMMServer.Properties.Resources.CustomTag_DroppedInfo;
                RepoFactory.CustomTag.Save(tag);

                // Pinned
                tag = new CustomTag();
                tag.TagName = JMMServer.Properties.Resources.CustomTag_Pinned;
                tag.TagDescription = JMMServer.Properties.Resources.CustomTag_PinnedInfo;
                RepoFactory.CustomTag.Save(tag);

                // Ongoing
                tag = new CustomTag();
                tag.TagName = JMMServer.Properties.Resources.CustomTag_Ongoing;
                tag.TagDescription = JMMServer.Properties.Resources.CustomTag_OngoingInfo;
                RepoFactory.CustomTag.Save(tag);

                // Waiting for Series Completion
                tag = new CustomTag();
                tag.TagName = JMMServer.Properties.Resources.CustomTag_SeriesComplete;
                tag.TagDescription = JMMServer.Properties.Resources.CustomTag_SeriesCompleteInfo;
                RepoFactory.CustomTag.Save(tag);

                // Waiting for Bluray Completion
                tag = new CustomTag();
                tag.TagName = JMMServer.Properties.Resources.CustomTag_BlurayComplete;
                tag.TagDescription = JMMServer.Properties.Resources.CustomTag_BlurayCompleteInfo;
                RepoFactory.CustomTag.Save(tag);
            }
            catch (Exception ex)
            {
                logger.Error( ex,"Could not Create Initial Custom Tags: " + ex.ToString());
            }
        }
    }
}