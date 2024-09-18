using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace FeedMe.Services
{
    public class Reddit
    {
        private readonly IConfigurationRoot _config;

        public Reddit(
            IConfigurationRoot config)
        {
            _config = config;
        }

        private static System.Timers.Timer timer;

        public int crawlInterval = 5;
        public async Task StartAsync()
        {
            //Grab crawlInterval from user defined config.
            string crawlConfigInterval = _config["options:crawlInterval"];
            bool success = int.TryParse(crawlConfigInterval, out int number);

            //If the user defined value is a valid integer, use it. Otherwise use the default value of 5 (minutes)
            if (success == true && number >= crawlInterval)
            {
                crawlInterval = number;
            }

            //Grab the most recent RSS feed of the subreddit, without waiting for the timer to elapse.
            InitialLatestPost();

            //Fire the event every x minutes (60000ms = 1 minute)
            timer = new System.Timers.Timer(crawlInterval * 60000);
            timer.Elapsed += GetLatestPostEvent;
            timer.AutoReset = true;
            timer.Enabled = true;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[INFO] FeedMe is now Online");
            Console.ResetColor();

            await Task.Delay(Timeout.Infinite);
        }

        private async void InitialLatestPost()
        {
            var fetchedData = await GetPostDataAsync();
            SendPostRequest(fetchedData);
        }

        private async void GetLatestPostEvent(object sender, ElapsedEventArgs e)
        {
            var fetchedData = await GetPostDataAsync();
            SendPostRequest(fetchedData);
        }

        private async Task<List<(string, string, string)>> GetPostDataAsync()
        {
            string address = _config["options:redditFeed"];
            string response = "";


            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0");
                client.DefaultRequestHeaders.Add("Accept", "text/html");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
                client.DefaultRequestHeaders.Add("Accept-Language", "en");
                client.DefaultRequestHeaders.Add("Cache-Control", "0");
                client.DefaultRequestHeaders.Add("Dnt", "1");

                using var httpResponse = await client.GetAsync(address).ConfigureAwait(false);
                var webBytes = await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                response = System.Text.Encoding.UTF8.GetString(webBytes);
            }

            string postEntries = response[response.IndexOf("</title><entry>")..];
            string[] entries = postEntries.Split("</entry><entry>");
            List<(string, string, string)> entryList = new();
            List<string> sentEntries = new();

            if (!File.Exists("SentData.txt")) { File.Create("SentData.txt").Close(); Thread.Sleep(2000); }

            sentEntries = File.ReadAllLines("SentData.txt").Where(x => !string.IsNullOrEmpty(x) || !x.Equals("\n")).ToList();
            

            foreach (var entry in entries)
            {
                string postId = GetBetween(entry, "<id>", "</id>");
                string title = GetBetween(entry, "<title>", "</title>");
                string externalUrl = GetBetween(entry, "&lt;/a&gt; &lt;br/&gt; &lt;span&gt;&lt;a href=&quot;", "&quot;");

                if (!sentEntries.Contains(postId))
                    entryList.Add((postId, title, externalUrl));

            }

            string[] oldData;
            if (sentEntries.Count() > 25)
            {
                oldData = sentEntries.Take(25 - entryList.Count).ToArray();
                File.Delete("SentData.txt");

                using (var writer = File.OpenWrite("SentData.txt"))
                {
                    foreach (var item in entryList)
                    {
                        byte[] bytes = Encoding.ASCII.GetBytes($"{item.Item1}\n");
                        writer.Write(bytes);
                    }
                    foreach (var entry in oldData)
                    {
                        byte[] oldEntry = Encoding.ASCII.GetBytes($"{entry}\n");
                        writer.Write(oldEntry);
                    }
                }
            }
            else
            {
                oldData = sentEntries.ToArray();
                File.Delete("SentData.txt");
                using (var writer = File.OpenWrite("SentData.txt"))
                {
                    foreach (var item in entryList)
                    {
                        byte[] bytes = Encoding.ASCII.GetBytes($"{item.Item1}\n");
                        writer.Write(bytes);
                    }
                    foreach (var entry in oldData)
                    {
                        byte[] oldEntry = Encoding.ASCII.GetBytes($"{entry}\n");
                        writer.Write(oldEntry);
                    }
                }
            }

            entryList.ToArray();

            return entryList;
        }

        private void SendPostRequest(List<(string, string, string)> postData)
        {
            //For each new post found, send an embed message to the discord webhook.
            postData.Reverse();
            foreach (var item in postData)
            {
                var client = new RestClient(_config["options:discordWebhook"]);
                var request = new RestRequest();

                string json = JsonConvert.SerializeObject(new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = Truncate(item.Item2, 256),
                            description = item.Item3
                        }
                    }
                });

                request.AddJsonBody(json);
                request.AddHeader("Content-Type", "application/json");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Entry {item.Item1}] {item.Item2}");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"External Url: {item.Item3}");
                Console.ResetColor();

                client.Post(request);

                //We must have a delay between sending each message to Discord so as not to trigger API ratelimit cooldowns.
                Thread.Sleep(5000);
            }
        }

        private static string GetBetween(string strSource, string strStart, string strEnd)
        {
            if (strSource.Contains(strStart) && strSource.Contains(strEnd))
            {
                int Start, End;
                Start = strSource.IndexOf(strStart, 0) + strStart.Length;
                End = strSource.IndexOf(strEnd, Start);
                return strSource[Start..End];
            }
            return "";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value[..maxLength];
        }



    }
}
