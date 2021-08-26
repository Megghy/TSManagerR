﻿using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TerrariaApi.Server;
using TShockAPI;

namespace TSManager.Modules
{
    public sealed partial class ServerManger : IDisposable
    {
        private static ServerManger Instance => Info.Server;
        private readonly Assembly mainasm;
        private readonly BlockingStream istream;
        //private readonly HashSet<Thread> ThreadList = new HashSet<Thread>();
        internal static HarmonyMethod GetMethod(string name)
        {
            return new(typeof(ServerManger).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }
        internal Color foregroundColor = ConsoleColor.White.ToColor();
        internal static readonly Harmony TSMHarmony = new("TSManager");
        public ServerManger(Assembly server)
        {
            mainasm = server;
            var console = typeof(Console);

            istream = new BlockingStream();
            Console.SetIn(new StreamReader(istream, Encoding.UTF8));
            Console.SetOut(new ConsoltOut());
            TSMHarmony.Patch(console.GetProperty("Title").SetMethod, GetMethod("OnSetTitle"));
            TSMHarmony.Patch(console.GetMethod("ResetColor"), GetMethod("OnResetColor"));
            TSMHarmony.Patch(console.GetProperty("ForegroundColor").SetMethod, GetMethod("OnSetForegroundColor"));
            TSMHarmony.Patch(console.GetMethod("Clear"), GetMethod("ClearText"));
        }
        static void OnServerStop(bool save = true, string reason = "Server shutting down!")
        {
            TSMMain.Instance.OnExit();
        }
        private static void ClearText()
        {
            TSMMain.GUIInvoke(() => TSMMain.GUI.Console_ConsoleBox.Document.Blocks.Clear());
        }
        private static bool OnSetForegroundColor(ConsoleColor value)
        {
            Console.Out.Flush();
            Instance.foregroundColor = value.ToColor();
            return false;
        }

        private static bool OnResetColor()
        {
            Console.Out.Flush();
            Instance.foregroundColor = ConsoleColor.White.ToColor();
            return false;
        }
        private static bool OnSetTitle(string value)
        {
            TSMMain.GUIInvoke(() => TSMMain.GUI.Console_MainGroup.Header = $"{value}");
            return false;
        }
        class TextInfo
        {
            public TextInfo(string text, bool isPlayer = false)
            {
                IsPlayer = isPlayer;
                Text = text;
            }
            public bool IsPlayer { get; set; }
            public string Text { get; set; }
            public override string ToString()
            {
                return Text;
            }
        }
        Color PlayerColor = Color.FromRgb(Microsoft.Xna.Framework.Color.Aquamarine.R, Microsoft.Xna.Framework.Color.Aquamarine.G, Microsoft.Xna.Framework.Color.Aquamarine.B);
        bool SplitTextFromName(string text, string name, out List<TextInfo> list)
        {
            list = new();
            if (text.Contains(name))
            {
                var splitText = text.Split(name, 2);
                list.Add(new TextInfo(splitText[0], false));
                list.Add(new TextInfo(name, true));
                list.Add(new TextInfo(splitText[1], false));
                return true;
            }
            else return false;
        }
        struct QueueInfo
        {
            public QueueInfo(Color color, string text)
            {
                Text = text;
                Color = color;
            }
            public string Text { get; set; }
            public Color Color { get; set; }
        }

        readonly ConcurrentQueue<QueueInfo> MessageQueue = new();
        public void OnGetText(string s, Color color = default)
        {
            MessageQueue.Enqueue(new(color == default ? foregroundColor : color, s));
        }
        internal void StartProcessText()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (MessageQueue.Count < 1)
                        {
                            Task.Delay(20).Wait();
                            continue;
                        }
                        if (MessageQueue.TryDequeue(out var info))
                        {
                            var s = info.Text;
                            var color = info.Color;
                            if (Info.IsEnterWorld && s != "\r\n")
                            {
                                var nameList = Info.Players.Select(p => p.Name).ToList();
                                var list = new List<TextInfo>() { new TextInfo(s, false) };
                                if (nameList.Any())
                                {
                                    nameList.ForEach(name =>
                                    {
                                        var tempList = new List<TextInfo>();
                                        list.ForEach(t =>
                                        {
                                            var text = t.Text;
                                            if (!t.IsPlayer)
                                            {
                                                while (SplitTextFromName(text, name, out var l))
                                                {
                                                    text = l[2].Text;
                                                    tempList.Add(l[0]);
                                                    tempList.Add(l[1]);
                                                }
                                                tempList.Add(new(text));
                                            }
                                            else tempList.Add(t);
                                        });
                                        list = tempList;
                                    });
                                    list.ForEach(t => TSMMain.GUI.Console_ConsoleBox.Add(t.Text, t.IsPlayer, t.IsPlayer ? PlayerColor : color));
                                }
                                else
                                    TSMMain.GUI.Console_ConsoleBox.Add(s, color);
                            }
                            else
                                TSMMain.GUI.Console_ConsoleBox.Add(s, color);
                        }
                    }
                    catch (Exception ex) { ex.ShowError(); }
                }
            });
        }
        public string[] GetStartArgs()
        {
            return new[]
            {
                $"-port",
                $"{TSMMain.Settings.Port}",
                $"-password",
                $"{TSMMain.Settings.Password}",
                $"-language",
                $"{(TSMMain.Settings.EnableChinese ? "zh-Hans" : " ")}",
                $"-world",
                //$"{(WorldCombo.SelectedIndex == -1 ? " " : ($"{WorldCombo.SelectedValue}"))}",
                $"{(File.Exists(TSMMain.Settings.World) ? TSMMain.Settings.World : "")}",
                $"-players",
                $"{TSMMain.Settings.MaxPlayer}"
            };
        }
        public void Stop()
        {
            Info.IsServerRunning = false;
            Info.GameThread.Abort();
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start(string[] param)
        {
            Task.Run(() =>
            {
                if (!Directory.Exists(Info.PluginPath) || !Directory.GetFiles(Info.PluginPath).Exist(f => Path.GetFileName(f) == "TShockAPI.dll"))
                {
                    Utils.Notice($"未检测到TShock程序集. TSManager暂不支持原版服务器", HandyControl.Data.InfoType.Error);
                    TSMMain.GUIInvoke(() =>
                    {
                        TSMMain.GUI.Console_StartServer.IsEnabled = true;
                    });
                    Info.IsServerRunning = false;
                    return;
                }
                Info.GameThread = new Thread(new ThreadStart(() =>
                {
                    try { mainasm.EntryPoint.Invoke(null, new object[] { param }); } catch (Exception ex) { Utils.Notice($"服务器崩溃\r\n{ex.Message}", HandyControl.Data.InfoType.Fatal); }
                }))
                {
                    IsBackground = true
                };
                Info.IsServerRunning = true;
                Info.GameThread.Start();

                ServerApi.Hooks.GamePostInitialize.Register(TSMMain.Instance, TSMMain.Instance.OnServerPostInitialize, -1);  //注册服务器加载完成的钩子
                TSMMain.Instance.OnServerPreInitializing();
                TSMMain.GUIInvoke(() => TSMMain.GUI.GoToStartServer.IsEnabled = false);
            });
        }

        public void ConsoleCommand(string text)
        {
            Utils.AddLine(text, Color.FromRgb(208, 171, 233));
            Commands.HandleCommand(TSPlayer.Server, Commands.Specifier + text);
        }
    }
    class ConsoltOut : TextWriter
    {
        public ConsoltOut()
        {
        }
        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }
        public override void Write(string value)
        {
            Info.Server.OnGetText(value);
        }
        public override void WriteLine(string value)
        {
            Info.Server.OnGetText(value + "\r\n");
        }
        public override void WriteLine()
        {
            Info.Server.OnGetText(" \r\n");
        }
    }
    public sealed partial class ServerManger
    {
        private class BlockingStream : Stream
        {
            private byte[] lasting = new byte[0];
            private readonly BlockingCollection<byte[]> queue = new();

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => 0;

            public override long Position { get; set; }

            public override void Flush() { }

            public void AppendText(string text)
            {
                queue.Add(Encoding.UTF8.GetBytes(text));
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (lasting.Length == 0)
                {
                    Console.Out.Flush();
                    lasting = queue.Take();
                }

                var n = lasting.Length;
                if (n > count)
                {
                    Buffer.BlockCopy(lasting, 0, buffer, 0, count);
                    lasting = lasting.Skip(count).ToArray();
                    return count;
                }
                else
                {
                    Buffer.BlockCopy(lasting, 0, buffer, 0, n);
                    lasting = new byte[0];
                    return n;
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => 0;

            public override void SetLength(long value) { }

            public override void Write(byte[] buffer, int offset, int count) { }
        }
    }
}
