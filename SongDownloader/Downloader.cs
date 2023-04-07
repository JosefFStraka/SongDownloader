using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Net;

namespace SongDownloader
{
    static class DownloaderStringExtension
    {
        public static string EncodeAsFileName(this string fileName)
        {
            return Regex.Replace(fileName, "[" + Regex.Escape(
                    new string(Path.GetInvalidFileNameChars())) + "]", " ");
        }
    }

    enum DownloaderStatus
    {
        StatusStarted = 0,
        StatusFinished,
        StatusFailed,
        StatusNotFound,
    }

    internal class Downloader
    {
        public delegate void DownloadProgressChangedEventHandler(string songName, int progressPercentage);
        public event DownloadProgressChangedEventHandler? DownloadProgressChanged;

        public delegate void DownloadProgressStatusEventHandler(string songName, DownloaderStatus status, object? data = null);
        public event DownloadProgressStatusEventHandler? DownloadProgressStatus;

        private static string PathToDownloadFolder = "";

        private static readonly HttpClient client = new HttpClient();

        private SemaphoreSlim semaphoreSlim = new(3, 3);

        public Downloader(string PathToDownloadFolder, int maxThreads)
        {
            semaphoreSlim = new SemaphoreSlim(maxThreads, maxThreads);
        }
        
        private static int _downloaded = 0;
        private static int _total = 0;
        
        public int Downloaded { get => _downloaded; }
        public int Total { get => _total; }

        public async Task<bool> DownloadSong(string songName, bool tagSong = true)
        {
            semaphoreSlim.Wait();

            try
            {
                Interlocked.Increment(ref _total);
                
                DownloadProgressStatus?.Invoke(songName, DownloaderStatus.StatusStarted);

                SongData? song = await SearchSong(songName);

                if (song != null)
                {
                    try
                    {
                        string songFileName = $"{songName.EncodeAsFileName()}.mp3";
                        string fullFilePath = Path.Combine(PathToDownloadFolder, songFileName);
                        await DownloadSongWithProgressAsync(song.url, fullFilePath, songName);
                        if (tagSong)
                            TagSong(song, fullFilePath);

                        Interlocked.Increment(ref _downloaded);
                        DownloadProgressStatus?.Invoke(songName, DownloaderStatus.StatusFinished);
                    }
                    catch (Exception ex)
                    {
                        DownloadProgressStatus?.Invoke(songName, DownloaderStatus.StatusFailed, ex);
                    }
                }
                else
                {
                    DownloadProgressStatus?.Invoke(songName, DownloaderStatus.StatusNotFound);
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                semaphoreSlim.Release();
            }

            return true;
        }

        public async Task<bool> DownloadSongWithProgressAsync(string songUrl, string savePath, string songName)
        {
            int oldProgressPercentage = -1;
            using (HttpClient client = new HttpClient())
            {
                using (HttpResponseMessage response = await client.GetAsync(songUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (FileStream fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                        {
                            long? totalBytes = response.Content.Headers.ContentLength;

                            byte[] buffer = new byte[4096];
                            int bytesRead = 0;
                            long totalBytesRead = 0;
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;
                                
                                if (totalBytes.HasValue)
                                {
                                    int progressPercentage = (int)Math.Round((double)totalBytesRead / totalBytes.Value * 100);
                                    if (oldProgressPercentage != progressPercentage)
                                    {
                                        //DownloadProgressChanged?.Invoke(songName, progressPercentage);
                                        oldProgressPercentage = progressPercentage;
                                    }
                                    //Console.Write($"\rDownloading {Path.GetFileName(savePath)}: {progressPercentage}%");
                                }
                                else
                                {
                                    //DownloadProgressChanged?.Invoke(songName, 100);
                                    //Console.Write($"\rDownloading {Path.GetFileName(savePath)}: {totalBytesRead / 1024} KB");
                                }
                            }
                        }
                    }
                }
            }

            return true;
        }

        public void TagSong(SongData song, string fullFilePath)
        {
            TagLib.File f = TagLib.File.Create(fullFilePath);
            f.Tag.Title = song.title;
            f.Tag.Performers = new string[] { song.artist };
            var duration = TimeSpan.FromSeconds((double)song.duration);
            string len = duration.ToString(@"hh\:mm\:ss");
            f.Tag.Length = len;
            f.Save();
        }

        public async Task<SongData?> SearchSong(string name)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,

            };

            string requestUri = "https://new.myfreemp3juices.cc/api/api_search.php";
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string?, string?>("q",  name.Replace(" ", "+")),
                new KeyValuePair<string?, string?>("sort", "2"),
                new KeyValuePair<string?, string?>("page", "0"),
            });

            var response = await client.PostAsync(requestUri, content);
            var responseString = await response.Content.ReadAsStringAsync();
            responseString = responseString[1..^4];
            var Rootobject = JsonSerializer.Deserialize<Rootobject>(responseString, options);

            if (Rootobject != null && (Rootobject?.response.Length ?? 0) > 1)
            {
                List<object> objects = Rootobject.response[1..^1].ToList();
                List<SongData?> songs = objects.Select(o => JsonSerializer.Deserialize<SongData>(o.ToString()!, options)).ToList();

                if (songs != null)
                {
                    return songs.FirstOrDefault(s => s != null);
                }
            }

            return null;
        }
    }
}
