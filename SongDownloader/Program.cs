using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace SongDownloader
{
    public class Rootobject
    {
        public object[] response { get; set; }
    }

    public class SongData
    {
        public string artist { get; set; }
        public int id { get; set; }
        public int owner_id { get; set; }
        public string title { get; set; }
        public int duration { get; set; }
        public string access_key { get; set; }
        public Ads ads { get; set; }
        public bool is_explicit { get; set; }
        public bool is_focus_track { get; set; }
        public bool is_licensed { get; set; }
        public string track_code { get; set; }
        public string url { get; set; }
        public int date { get; set; }
        public Main_Artists[] main_artists { get; set; }
        public bool short_videos_allowed { get; set; }
        public bool stories_allowed { get; set; }
        public bool stories_cover_allowed { get; set; }
    }

    public class Main_Artists
    {
        public string name { get; set; }
        public string domain { get; set; }
        public string id { get; set; }
        public bool is_followed { get; set; }
        public bool can_follow { get; set; }
    }

    public class Ads
    {
        public string content_id { get; set; }
        public string duration { get; set; }
        public string account_age_type { get; set; }
        public string puid1 { get; set; }
        public string puid22 { get; set; }
    }

    static class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static string PathToFolder = @"C:\Users\straj\Desktop\songs";
        private static SemaphoreSlim semaphoreSlim = new(10,10);

        private static int Downloaded = 0;
        private static int Total = 0;

        public static async Task Main(string[] args)
        {
            string[] lines = File.ReadAllLines(args[1]);


            Downloaded = 0;
            Total = 0;

            List<Task> downloadTasks = new List<Task>();

            for (int i = 0; i < lines.Length; i++)
            {
                string songName = lines[i];

                if (String.IsNullOrEmpty(songName))
                    continue;

                downloadTasks.Add(DownloadSong(songName));
            }

            await Task.WhenAll(downloadTasks);

            Console.WriteLine($"Download finished {Downloaded}/{Total}.");
            Console.ReadLine();
        }

        private static async Task<bool> DownloadSong(string songName)
        {
            semaphoreSlim.Wait();

            try
            {
                Interlocked.Increment(ref Total);

                Console.WriteLine($"{songName}: Downloading");

                SongData? song = await SearchSong(songName);

                if (song != null)
                {
                    try
                    {
                        string songFileName = $"{songName.EncodeAsFileName()}.mp3";
                        string fullFilePath = Path.Combine(PathToFolder, songFileName);
                        await DownloadSongWithProgressAsync(song.url, fullFilePath);
                        TagSong(song, fullFilePath);

                        Interlocked.Increment(ref Downloaded);
                        Console.WriteLine($"{songName}: Downloaded \"{songFileName}\"");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{songName}: Error downloading song ({ex.Message})");
                    }
                }
                else
                {
                    Console.WriteLine($"{songName}: Song not found");
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

        private static async Task<bool> DownloadSongWithProgressAsync(string songUrl, string savePath)
        {
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
                                    //Console.Write($"\rDownloading {Path.GetFileName(savePath)}: {progressPercentage}%");
                                }
                                else
                                {
                                    //Console.Write($"\rDownloading {Path.GetFileName(savePath)}: {totalBytesRead / 1024} KB");
                                }
                            }
                        }
                    }
                }
            }

            return true;
        }

        private static void TagSong(SongData song, string fullFilePath)
        {
            TagLib.File f = TagLib.File.Create(fullFilePath);
            f.Tag.Title = song.title;
            f.Tag.Performers = new string[] { song.artist };
            var duration = TimeSpan.FromSeconds((double)song.duration);
            string len = duration.ToString(@"hh\:mm\:ss");
            f.Tag.Length = len;
            f.Save();
        }

        private static async Task<SongData?> SearchSong(string name)
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

        public static string EncodeAsFileName(this string fileName)
        {
            return Regex.Replace(fileName, "[" + Regex.Escape(
                    new string(Path.GetInvalidFileNameChars())) + "]", " ");
        }
    }
}