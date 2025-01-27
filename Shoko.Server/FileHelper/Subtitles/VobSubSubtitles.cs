﻿using System;
using System.Collections.Generic;
using System.IO;
using Shoko.Models.MediaInfo;
using Shoko.Server.Utilities.MediaInfoLib;

namespace Shoko.Server.FileHelper.Subtitles;

public class VobSubSubtitles : ISubtitles
{
    public List<TextStream> GetStreams(FileInfo file)
    {
        var streams = new List<TextStream>();
        var language = SubtitleHelper.GetLanguageFromFilename(file.Name);

        var m = MediaInfo.GetMediaInfo(file.FullName);
        var tStreams = m?.TextStreams;
        if (tStreams == null || tStreams.Count <= 0)
        {
            return streams;
        }

        tStreams.ForEach(a =>
        {
            a.External = true;
            a.Filename = file.Name;
            if (language == null)
            {
                return;
            }

            a.Language = language;
            var mapping = MediaInfoUtils.GetLanguageMapping(language);
            if (mapping == null)
            {
                return;
            }

            a.LanguageCode = mapping.Item1;
            a.LanguageName = mapping.Item2;
        });
        streams.AddRange(tStreams);
        return streams;
    }

    public bool IsSubtitleFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower().TrimStart('.');
        return ext.Equals("idx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals("sub", StringComparison.OrdinalIgnoreCase);
    }
}
