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
        public static async Task<int> Main(string[] args)
        {
            string pathToSongListFile = "";
            string pathToDownloadFolder = "";

            if (args.Length != 2)
            {
                Console.WriteLine("SongDownloader.exe \"path to song list\" \"path to download folder\"");
                do
                {
                    Console.Write("path to song list:");
                    string input = Console.ReadLine() ?? "";
                    input = input.Replace("\"", "");
                    if (File.Exists(input))
                    {
                        pathToSongListFile = input;
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Invalid file");
                    }
                } while (true);
                do
                {
                    Console.Write("path to download folder:");
                    string input = Console.ReadLine() ?? "";
                    input = input.Replace("\"", "");
                    if (Directory.Exists(input))
                    {
                        pathToDownloadFolder = input;
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Invalid folder");
                    }
                } while (true);
            }
            else
            {
                pathToSongListFile = args[0];
                pathToDownloadFolder = args[1];
            }

            string[] lines = File.ReadAllLines(pathToSongListFile);

            Downloader Downloader = new(pathToDownloadFolder, 5);
            Downloader.DownloadProgressStatus += (string songName, DownloaderStatus status, object? data) =>
            {
                switch (status)
                {
                    case DownloaderStatus.StatusStarted:
                        Console.WriteLine($"Downloading: {songName}");
                        break;
                    case DownloaderStatus.StatusFinished:
                        Console.WriteLine($"Downloaded: {songName}");
                        break;
                    case DownloaderStatus.StatusFailed:
                        Exception? ex = (data as Exception);
                        string e = ex?.Message ?? "";
                        Console.WriteLine($"Error downloading song: {songName} ({e})");
                        break;
                    case DownloaderStatus.StatusNotFound:
                        Console.WriteLine($"Song not found: {songName}");
                        break;
                    default:
                        break;
                }
            };

            List<Task> downloadTasks = new List<Task>();

            List<string> files = Directory.EnumerateFiles(pathToDownloadFolder).Select(s => Path.GetFileNameWithoutExtension(s).EncodeAsFileName()).ToList();

            for (int i = 0; i < lines.Length; i++)
            {
                string songName = lines[i];

                if (String.IsNullOrEmpty(songName))
                    continue;

                if (files.Contains(songName))
                {
                    string fullFilePath = Path.Combine(pathToDownloadFolder, $"{songName}.mp3");
                    FileInfo fi = new FileInfo(fullFilePath);
                    if (fi.Length > 0)
                    {
                        Console.WriteLine($"Skipping: {songName}");
                        continue;
                    }
                }

                downloadTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await Downloader.DownloadSong(songName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Critical error: {songName} ({ex.Message})");
                    }
                }));
            }

            await Task.WhenAll(downloadTasks);

            Console.WriteLine($"Download finished {Downloader.Downloaded}/{Downloader.Total}.");
            Console.ReadLine();

            return 0;
        }

    }
}