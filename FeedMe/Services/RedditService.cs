using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.IO;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace FeedMe.Services
{
    public class RedditService
    {
        private readonly IConfigurationRoot _config;

        public RedditService(
            IConfigurationRoot config)
        {
            _config = config;
        }

        private static System.Timers.Timer timer;
        public static string rootPath = AppDomain.CurrentDomain.BaseDirectory;

        //It is recommend that you do not crawl faster than every 3 minutes.
        //Due to the rate limiting of requests, it takes approx 2.5 minutes to send a full batch of new posts to discord.
        public int crawlInterval = 3;
        public async Task StartAsync()
        {
            //Grab crawlInterval from user defined config.
            string crawlConfigInterval = _config["options:crawlInterval"];
            int number;
            bool success = Int32.TryParse(crawlConfigInterval, out number);

            //If the user defined value is a valid integer, use it. Otherwise use the default value of 5 (minutes)
            if (success == true)
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

            await Task.Delay(Timeout.Infinite);
        }

        private void InitialLatestPost()
        {
            var fetchedData = GetPostData();
            SendPostRequest(fetchedData);
        }

        private void GetLatestPostEvent(object sender, ElapsedEventArgs e)
        {
            var fetchedData = GetPostData();
            SendPostRequest(fetchedData);
        }

        //The strings are in the order of "postId", "postTitle" and "postExternalUrl"
        private List<(string, string, string)> GetPostData()
        {
            //Address is the feed in which we are web scraping.
            string address = _config["options:redditFeed"];
            string response = "";

            //Download the latest data. The feed we are using will provide the 25 most recent posts.
            using (var client = new WebClient())
            {
                response = client.DownloadString(address);
            }

            //Remove the top pinned post (usually the subreddit introduction or rules post)
            string postEntries = response.Substring(response.IndexOf("</title><entry>"));

            

            //Create a list of the remaining valid post entries
            string[] entries = postEntries.Split("</entry><entry>");
            List<(string, string, string)> entryList = new();
            List<string> sentEntries = new();

            //If this file exists, read data from it. This file contains the previous 25 post entry IDs so as not to send duplicate messages to the discord server
            if (File.Exists("SentData.txt"))
            {
                sentEntries = File.ReadAllLines("SentData.txt").ToList();
            }

            //Using the list of posts created earlier, format and extract only the needed data, then add it to its own list
            foreach (var entry in entries)
            {
                string postId = GetBetween(entry, "<id>", "</id>");
                string title = GetBetween(entry, "<title>", "</title>");
                string externalUrl = GetBetween(entry, "&lt;/a&gt; &lt;br/&gt; &lt;span&gt;&lt;a href=&quot;", "&quot;");

                if (!sentEntries.Contains(postId))
                    entryList.Add((postId, title, externalUrl));

                Console.WriteLine($"[Entry {postId}] Title: {title}");
            }

            var reorderedList = entryList.ToArray().Reverse();

            //Record IDs of the data we are about to send to discord.
            foreach (var item in reorderedList)
            {
                File.AppendAllText("SentData.txt", $"{item.Item1}" + Environment.NewLine);
            }

            //Grab the total number of lines in the file. This is useful only if the file already exists.
            //It will try to always keep the most recent 25 post IDs in the file, in case there is less than 25 posted per day, again to avoid posting duplicates.
            //It will remove the oldest line once the threshold is met to conserve disk space and resources.
            var pastEntries = File.ReadAllLines("SentData.txt").Where(x => !string.IsNullOrEmpty(x)).ToArray();

            //Console.WriteLine(pastEntries.Count());

            if (pastEntries.Count() > 25)
                File.WriteAllLines("SentData.txt", pastEntries.Reverse().Take(25).Reverse());

            entryList.ToArray();

            return entryList;
        }

        private void SendPostRequest(List<(string, string, string)> postData)
        {
            //string[] pastEntriesCount = File.ReadAllLines("SentData.txt");
            //For each new post found, send an embed message to the discord webhook.
            //TODO reverse list
            foreach (var item in postData)
            {
                //if (pastEntriesCount.Contains(item.Item1.ToLower().Trim()))
                //{
                //    return;
                //}
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
                Console.WriteLine($"ID:  {item.Item1} | Title: {item.Item2}");
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
                return strSource.Substring(Start, End - Start);
            }
            return "";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
