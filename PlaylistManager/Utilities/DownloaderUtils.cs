﻿using BeatSaverSharp;
using BeatSaverSharp.Models;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

/*
 * Original Author: KyleMC1413
 * Adapted from BeatSaverDownloader
 */

namespace PlaylistManager.Utilities
{
    internal class DownloaderUtils
    {
        private BeatSaver beatSaverInstance;
        public static DownloaderUtils instance;
        public static void Init()
        {
            instance = new DownloaderUtils();
            BeatSaverOptions options = new BeatSaverOptions(applicationName: typeof(DownloaderUtils).Assembly.GetName().Name, version: typeof(DownloaderUtils).Assembly.GetName().Version);
            instance.beatSaverInstance = new BeatSaver(options);
        }

        private async Task BeatSaverBeatmapDownload(Beatmap song, BeatmapVersion songversion, CancellationToken token, IProgress<double> progress = null)
        {
            string customSongsPath = CustomLevelPathHelper.customLevelsDirectoryPath;
            if (!Directory.Exists(customSongsPath))
            {
                Directory.CreateDirectory(customSongsPath);
            }
            var zip = await songversion.DownloadZIP(token, progress).ConfigureAwait(false);
            
            await ExtractZipAsync(zip, customSongsPath, songInfo: song).ConfigureAwait(false);
        }

        public async Task<string> BeatmapDownloadByKey(string key, CancellationToken token, IProgress<double> progress = null)
        {
            bool songDownloaded = false;
            while(!songDownloaded)
            { 
                try
                {
                    var song = await beatSaverInstance.Beatmap(key, token);
                    // A key is not enough to identify a specific version. So just get the latest one.
                    if (SongCore.Loader.GetLevelByHash(song.LatestVersion.Hash) == null)
                    {
                        await BeatSaverBeatmapDownload(song, song.LatestVersion, token, progress);
                    }
                    songDownloaded = true;
                    return song.LatestVersion.Hash;
                }
                catch (Exception e)
                {
                    if (!(e is TaskCanceledException))
                    {
                        Plugin.Log.Critical(string.Format("Failed to download Song {0}. Exception: {1}", key, e.ToString()));
                    }
                    songDownloaded = true;
                }
            }
            return "";
        }

        public async Task BeatmapDownloadByHash(string hash, CancellationToken token, IProgress<double> progress = null)
        {
            bool songDownloaded = false;
            while (!songDownloaded)
            {
                try
                {
                    var song = await beatSaverInstance.BeatmapByHash(hash, token);
                    if (song == null)
                    {
                        Plugin.Log.Critical(string.Format("Failed to download Song {0}. Unable to find a beatmap for that hash.", hash));
                        return;
                    }

                    BeatmapVersion matchingVersion = null;
                    foreach (BeatmapVersion version in song.Versions)
                    {
                        if (hash.ToLowerInvariant() == version.Hash.ToLowerInvariant())
                        {
                            matchingVersion = version;
                        }
                    }
                    
                    // Just download exact hash matches for now. Updating to a newer version of a song based on a hash should require some user interaction or option setting.
                    if (matchingVersion != null)
                    {
                        await BeatSaverBeatmapDownload(song, matchingVersion, token, progress);
                    }
                    else
                    {
                        Plugin.Log.Critical(string.Format("Failed to download Song {0}. Unable to find a matching version for that hash.", hash));
                    }
                    songDownloaded = true;
                }
                catch (Exception e)
                {
                    if (!(e is TaskCanceledException))
                    {
                        Plugin.Log.Critical(string.Format("Failed to download Song {0}. Exception: {1}", hash, e.ToString()));
                    }
                    songDownloaded = true;
                }
            }
        }

        public async Task BeatmapDownloadByCustomURL(string url, string songName, CancellationToken token)
        {
            try
            {
                string customSongsPath = CustomLevelPathHelper.customLevelsDirectoryPath;
                if (!Directory.Exists(customSongsPath))
                {
                    Directory.CreateDirectory(customSongsPath);
                }
                var zip = await DownloadFileToBytesAsync(url, token);
                await ExtractZipAsync(zip, customSongsPath, songName: songName).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!(e is TaskCanceledException))
                    Plugin.Log.Critical(string.Format("Failed to download Song {0}", url));
            }
        }

        private async Task ExtractZipAsync(byte[] zip, string customSongsPath, bool overwrite = false, string songName = null, Beatmap songInfo = null)
        {
            Stream zipStream = new MemoryStream(zip);
            try
            {
                ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                string basePath = "";
                if (songInfo != null)
                {
                    basePath = songInfo.ID + " (" + songInfo.Metadata.SongName + " - " + songInfo.Metadata.LevelAuthorName + ")";
                }
                else
                {
                    basePath = songName;
                }
                basePath = string.Join("", basePath.Split(Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToArray()));
                string path = Path.Combine(customSongsPath, basePath);
                
                if (!overwrite && Directory.Exists(path))
                {
                    int pathNum = 1;
                    while (Directory.Exists(path + $" ({pathNum})")) ++pathNum;
                    path += $" ({pathNum})";
                }
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                await Task.Run(() =>
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Name))
                        {
                            var entryPath = Path.Combine(path, entry.Name); // Name instead of FullName for better security and because song zips don't have nested directories anyway
                            if (overwrite || !File.Exists(entryPath)) // Either we're overwriting or there's no existing file
                                entry.ExtractToFile(entryPath, overwrite);
                        }
                    }
                }).ConfigureAwait(false);
                archive.Dispose();
            }
            catch (Exception e)
            {
                Plugin.Log.Critical($"Unable to extract ZIP! Exception: {e}");
                return;
            }
            zipStream.Close();
        }

        public async Task<byte[]> DownloadFileToBytesAsync(string url, CancellationToken token)
        {
            Uri uri = new Uri(url);
            using (var webClient = new WebClient())
            using (var registration = token.Register(() => webClient.CancelAsync()))
            {
                var data = await webClient.DownloadDataTaskAsync(uri);
                return data;
            }
        }
    }
}
