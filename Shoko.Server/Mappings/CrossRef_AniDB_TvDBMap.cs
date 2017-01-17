﻿using FluentNHibernate.Mapping;
using Shoko.Models.Server;
using Shoko.Server.Models;

namespace Shoko.Server.Mappings
{
    public class CrossRef_AniDB_TvDBMap : ClassMap<CrossRef_AniDB_TvDB>
    {
        public CrossRef_AniDB_TvDBMap()
        {
            Table("CrossRef_AniDB_TvDB");

            Not.LazyLoad();
            Id(x => x.CrossRef_AniDB_TvDBID);

            Map(x => x.AnimeID).Not.Nullable();
            Map(x => x.CrossRefSource).Not.Nullable();
            Map(x => x.TvDBID).Not.Nullable();
            Map(x => x.TvDBSeasonNumber).Not.Nullable();
        }
    }
}