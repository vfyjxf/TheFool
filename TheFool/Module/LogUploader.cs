using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Lagrange.Core;
using Lagrange.Core.Common.Interface.Api;
using Lagrange.Core.Event;
using Lagrange.Core.Event.EventArg;
using Lagrange.Core.Message;
using Lagrange.Core.Message.Entity;

namespace TheFool.Module;

public static class LogUploader
{
    private const string McLogs = "https://api.mclo.gs/1/log";

    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly string[] SupportTypes =
    {
        "txt", "log", "zs", "java", "groovy"
    };

    private static readonly string[] ZipTypes =
    {
        "zip", "7z"
    };

    private static readonly string[] ZipFilePrefix =
    {
        "错误报告", "minecraft-exported"
    };

    private static readonly string[] ValidLogPrefix =
    {
        "latest", "crash", "crafttweaker", "debug"
    };

    private static readonly string[] AllTypes = SupportTypes.Concat(ZipTypes).ToArray();

    public static async void OnFriendMessageReceived(BotContext bot, EventBase eventArgs)
    {
        MessageChain? rawChain = null;
        MessageBuilder? nextMessage = null;
        switch (eventArgs)
        {
            case FriendMessageEvent messageEvent:
                rawChain = messageEvent.Chain;
                nextMessage = MessageBuilder.Friend(rawChain.FriendUin);
                break;
            case GroupMessageEvent groupMessageEvent:
                rawChain = groupMessageEvent.Chain;
                nextMessage = MessageBuilder.Group(rawChain.GroupUin.GetValueOrDefault());
                break;
        }

        if (rawChain == null || nextMessage == null) return;
        var last = rawChain.Last();
        if (last is not FileEntity fileEntity) return;
        var fileInfo = new FileInfo(fileEntity.FileName);
        var fileExtension = fileInfo.Extension.Replace(".", "");
        var supportType =
            (from type in AllTypes
                where fileExtension.Equals(type)
                select type).FirstOrDefault();
        if (supportType == null)
        {
            Console.WriteLine($"Unsupported file type: {fileInfo.Extension}");
            return;
        }

        var fileUrl = fileEntity.FileUrl;
        if (fileUrl == null || !fileUrl.Any())
        {
            Console.WriteLine("Cannot get file url!");
            await bot.SendMessage(nextMessage.Forward(rawChain).Text("无法获取文件链接!").Build());
            return;
        }

        var isZip = ZipTypes.Contains(fileExtension);
        if (!isZip)
        {
            if (fileEntity.FileSize > 10485760) //10mib
            {
                Console.WriteLine("File is larger than 10MiB!");
                await bot.SendMessage(nextMessage.Forward(rawChain).Text("文件过大，请确保文件小于10Mib!")
                    .Build());
                return;
            }

            var content = Client.GetStringAsync(fileUrl).Result;
            await UploadFile(content).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    var text = result == null ? "上传失败!" : $"上传成功：{result}";
                    var nextChain = nextMessage.Forward(rawChain).Text(text).Build();
                    bot.SendMessage(nextChain);
                }
                else
                {
                    Console.WriteLine(task.Exception);
                }
            });
        }
        else
        {
            var crashReport = ZipFilePrefix.Any(prefix => fileInfo.Name.Contains(prefix));
            if (!crashReport) return;
            var zipBytes = Client.GetByteArrayAsync(fileEntity.FileUrl);
            using var zipStream = new MemoryStream(zipBytes.Result);
            using var archive = new ZipArchive(zipStream);
            List<string> messages = new();
            foreach (var entry in archive.Entries)
            {
                var entryInfo = new FileInfo(entry.Name);
                var entryExtension = entryInfo.Extension.Replace(".", "");
                var log = ValidLogPrefix.Any(prefix => entry.Name.Contains(prefix));
                if (!SupportTypes.Contains(entryExtension) || !log) continue;
                await using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                var content = await reader.ReadToEndAsync();
                await UploadFile(content).ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        var result = task.Result;
                        var text = result == null ? $"{fileInfo.Name} : 上传失败!" : $"{entryInfo.Name} : {result}";
                        messages.Add(text);
                    }
                    else
                    {
                        Console.WriteLine(task.Exception);
                    }
                });
            }

            if (!messages.Any()) return;
            nextMessage.Forward(rawChain);
            var builder = new StringBuilder();
            foreach (var message in messages) builder.AppendLine(message);
            await bot.SendMessage(nextMessage.Text(builder.ToString()).Build());
        }
    }

    private static async Task<string?> UploadFile(string contents)
    {
        var content = new StringContent($"content={Uri.EscapeDataString(contents)}", Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var response = await Client.PostAsync(McLogs, content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var jsonObj = JsonNode.Parse(json)?.AsObject();
        if (jsonObj != null) return jsonObj["url"]?.GetValue<string>() ?? null;
        Console.WriteLine($"Cannot parse response json: {json}");
        return null;
    }
}