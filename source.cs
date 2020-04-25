using System;
using System.Net;
using System.IO;
using System.Text;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Reflection;
using System.Security.Permissions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Utilities
{
    public class FileRecord {
        public string author;
        public string date;
        public string filename;
        public int bytesize;
        public bool displayable;
    }
    public class RecordBook {
        public List<FileRecord> records = new List<FileRecord>();
    }

    public class CustomCommand : System.Attribute {
        public string command;
        public bool isAsync;
        public CustomCommand(string c, bool a)
        {
            command = c;
            isAsync = a;
        }
    }
}

namespace Logbot
{
    class Bot
    {
        Dictionary<string, Tuple<bool, MethodInfo>> cmds;
        Utilities.RecordBook records;
        #region boringVars
        private string _discordToken = "botsecret";
        //Directory to where logs must be stored
        private string _logdir = @"Pathtologdir";
        //Base directory of the bot
        private string _basedir =  @"Pathtodir";
        //Base file extension for WIN users
        private string _suffix = ".txt";
        private string _mdprefix = @"```Markdown";
        private string _mdsuffix = @"```";
        private int _MAXDISCORDLENGTH = 1950; //Max amount of characters before Discord denies the message
        #endregion
        private DiscordSocketClient _client;
        private ISocketMessageChannel channel;
        private char _PREFIX = '!';
        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
        //User input cannot be trusted. The clowns may use emotes as filenames or some shit
        private string sf(string str)
        {
            return Regex.Replace(str, @"[^0-9a-zA-Z!. ]","");
        }
        private async Task<bool> ReqParam(int req, int actual)
        {
            if(actual == req)
                return true;

            await this.channel.SendMessageAsync($"You provided {(actual)} args, but the command requires {(req)}!");
            return false;
        }

        [Utilities.CustomCommand("echo", true)]
        public async Task PrintStuff(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;

            await this.channel.SendMessageAsync((string)((Object[])args[0])[0]);
        }

        [Utilities.CustomCommand("new", true)]
        public async Task UploadFile(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;

            string filename = (string)((Object[])args[0])[0];
            string url = (string)args[1];
            string user = (string)args[2];
            string date = (string)args[3];
            string tar = $@"{_logdir}{filename}{_suffix}";
            if(File.Exists(tar))
            {
                await this.channel.SendMessageAsync($"File with name '{filename}' already exists!");
                return;
            }
            WebClient client = new WebClient();
            string content = Encoding.UTF8.GetString(client.DownloadData(url));
            int contentSize = content.Length + _mdprefix.Length + _mdsuffix.Length;
            bool displayable = contentSize < _MAXDISCORDLENGTH;
            using(StreamWriter outPut = File.AppendText(tar))
            {
                outPut.WriteLine(_mdprefix);
                outPut.WriteLine(content);
                outPut.WriteLine(_mdsuffix);
            }

            records.records.Add(new Utilities.FileRecord(){
                author = user,
                filename = filename,
                date = date,
                bytesize = contentSize,
                displayable = displayable
            });
            RewriteJson();
            await this.channel.SendMessageAsync($"File with name '{filename}' has been created!");
        }
        private void RewriteJson()
        {
            using (StreamWriter file = File.CreateText($"{_basedir}records.json"))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, records);
            }
        }

        [Utilities.CustomCommand("show", true)]
        public async Task ShowFile(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;
            string filename = (string)((Object[])args[0])[0];
            string tar = $@"{_logdir}{filename}{_suffix}";
            if(!File.Exists(tar))
            {
                await this.channel.SendMessageAsync($"File with name '{filename}' does not exists!");
                return;
            }
            string content = File.ReadAllText(tar);
            if(content.Length > _MAXDISCORDLENGTH)
                await this.channel.SendFileAsync(tar);
            else
                await this.channel.SendMessageAsync(File.ReadAllText(tar));
        }

        [Utilities.CustomCommand("at", true)]
        public async Task ShowIndexfile(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;
            int index;
            bool parse = Int32.TryParse((string)((Object[])args[0])[0], out index);
            if(!parse || index < 0 || index >= records.records.Count)
            {
                await this.channel.SendMessageAsync($"File is invalid!");
                return;
            }
            string filename = records.records[index].filename;
            string tar = $@"{_logdir}{filename}{_suffix}";
            if(!File.Exists(tar))
            {
                await this.channel.SendMessageAsync($"File with name '{filename}' does not exists!");
                return;
            }
            string content = File.ReadAllText(tar);
            if(content.Length > _MAXDISCORDLENGTH)
                await this.channel.SendFileAsync(tar);
            else
                await this.channel.SendMessageAsync(File.ReadAllText(tar));
        }

        [Utilities.CustomCommand("info", true)]
        public async Task ShowAuthor(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;
            string filename = (string)((Object[])args[0])[0];
            int index = FindRecord(filename);
            if(!(index>=0))
            {
                await this.channel.SendMessageAsync($"File with name '{filename}' does not exists!");
                return;
            }
            string response = $"{filename} was written by {records.records[index].author} at {records.records[index].date}. Displayable : {records.records[index].displayable}. Length: {records.records[index].bytesize}";
            await this.channel.SendMessageAsync(response);
        }

        [Utilities.CustomCommand("list", true)]
        public async Task ListLogs(params Object[] args)
        {
            Task<bool> c = ReqParam(0, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;
            if(records.records.Count == 0)
            {
                await this.channel.SendMessageAsync("No logs available!");
                return;               
            }
            string response = "```";
            int count = 0;
            foreach(Utilities.FileRecord f in records.records)
            {
                response = response + $"[{count}] {f.filename} - Written by {f.author} ({(f.displayable ? "short" : "long")})\n";
                ++count;
            }
            response = response + "```";
            await this.channel.SendMessageAsync(response);
        }

        [Utilities.CustomCommand("delete", true)]
        public async Task DeleteLog(params Object[] args)
        {
            Task<bool> c = ReqParam(1, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;

            string filename = (string)((Object[])args[0])[0];
            string user = (string)args[1];
            string tar = $"{_logdir}{filename}{_suffix}";
            int index = FindRecord(filename);
            if(!(index>=0) || !File.Exists(tar))
            {
                await this.channel.SendMessageAsync($"File with name '{filename}' does not exists!");
                return;
            }
            if(!(records.records[index].author == user))
            {
                await this.channel.SendMessageAsync($"You can't delete '{filename}' as you did not write it!");
                return;
            }
            records.records.RemoveAt(index);
            File.Delete(tar);
            RewriteJson();
            await this.channel.SendMessageAsync($"File with name '{filename}' was deleted!");
        }

        [Utilities.CustomCommand("markdown", true)]
        public async Task ShowDocs(params Object[] args)
        {
            Task<bool> c = ReqParam(0, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;

            string mdlink = @"https://support.discordapp.com/hc/en-us/articles/210298617-Tekst-opmaken-met-markdown-dikgedrukt-cursief-onderlijnen-";
            await this.channel.SendMessageAsync(mdlink);
        }

        [Utilities.CustomCommand("source", true)]
        public async Task ShowSource(params Object[] args)
        {
            Task<bool> c = ReqParam(0, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;

            string sclink = @"https://github.com/PiepsC/DiscordLoggingbot.git";
            await this.channel.SendMessageAsync(sclink);
        }
        
        [Utilities.CustomCommand("help", true)]
        public async Task ShowHelp(params Object[] args)
        {
            Task<bool> c = ReqParam(0, ((Object[])args[0]).Length);
            await c;
            if(!c.Result)
                return;
            string tar = $"{_basedir}help{_suffix}";
            if(!File.Exists(tar))
            {
               await this.channel.SendMessageAsync("Help file unavailable!");
               return;
            }

            string response = File.ReadAllText(tar);
            await this.channel.SendMessageAsync(response);
        }
        private int FindRecord(string tar)
        {
            int i = 0;
            foreach(Utilities.FileRecord f in records.records)
            {
                if(f.filename == tar)
                    return i;
                ++i;
            }
            return -1;
        }
        private async Task Initialize()
        {
            _client = new DiscordSocketClient(
                new DiscordSocketConfig{
                    LogLevel = LogSeverity.Info,
                    MessageCacheSize = 50
                }
            );
            _client.Log += Log;
            _client.MessageReceived += ParseMessage;
            await _client.LoginAsync(TokenType.Bot, _discordToken);
            await _client.StartAsync();
            await Task.Delay(-1); //Loop indefinitely asnyc
        }
        private async Task ParseMessage(SocketMessage message)
        {
            channel = message.Channel; //Lock in channel for replies
            string command = sf(message.Content);
            string user = message.Author.Username;
            string date = message.CreatedAt.ToString();
            string[] args;
            var attachments = message.Attachments;

            if(command.Length > 1 && command[0] == _PREFIX)
            {
                args = command.Substring(1).Split(null).Where(inp => inp.Length>0).ToArray();
                if(cmds.ContainsKey(args[0]))
                {
                    if(attachments.Count>0)
                    {
                        await Execute(cmds[args[0]], args.Skip(1).ToArray(),
                        attachments.ElementAt(0).Url, user, date);
                    }
                    else
                        await Execute(cmds[args[0]], args.Skip(1).ToArray(), user, date);
                }
            }
        }
        //Execute a command
        private async Task Execute(Tuple<bool, MethodInfo> m, params Object[] args)
        {
            if(!m.Item1)
                m.Item2.Invoke(this, new Object[]{args}); //Not async
            else
            {
                Task t = (Task) m.Item2.Invoke(this, new Object[]{args}); //Async
                await t;
            }
        }
        private void LoadJson(dynamic tar)
        {
            foreach(dynamic s in tar.records)
            {
                records.records.Add(new Utilities.FileRecord(){
                    author = s.author,
                    filename = s.filename,
                    date = s.date,
                    bytesize = s.bytesize,
                    displayable = s.displayable
                });
            }
        }
        public void Init(Dictionary<string, Tuple<bool, MethodInfo>> cmds)
        {
            this.cmds=cmds;
            if(!File.Exists($"{_basedir}records.json"))
                records = new Utilities.RecordBook();
            else
            {
                records = new Utilities.RecordBook();
                LoadJson(JObject.Parse(File.ReadAllText($"{_basedir}records.json")));
            }
        }
        public void Run()
        => Initialize().GetAwaiter().GetResult();
    }
    static class Executor{
            static Dictionary<string, Tuple<bool, MethodInfo>> GenCommands(MethodInfo[] a, bool verbose=true)
            {
                Dictionary<string, Tuple<bool, MethodInfo>> result = new Dictionary<string, Tuple<bool, MethodInfo>>();
                foreach(MethodInfo tar in a)
                {
                    Utilities.CustomCommand cmd = (Utilities.CustomCommand)tar.GetCustomAttributes(typeof(Utilities.CustomCommand),true)[0]; //No duplicate attributes per cmd
                    result.Add(new string(cmd.command), new Tuple<bool, MethodInfo>(cmd.isAsync, tar));
                    if(verbose)
                    {
                        string method = tar.ToString().Split(null)[1].Split('(')[0];
                        Console.WriteLine($"New command: {cmd.command}({method})\nIs async: {cmd.isAsync}");
                    }
                }
                return result;
            }
            static void Main(string[] args){
            Bot obj = new Bot();
            Assembly assem = typeof(Bot).Assembly;
            MethodInfo[] methods = assem.GetTypes()
                    .SelectMany(t => t.GetMethods())
                    .Where(m => m.GetCustomAttributes(typeof(Utilities.CustomCommand), false).Length > 0)
                    .ToArray();
            obj.Init(GenCommands(methods));
            obj.Run();
        }
    }
}
