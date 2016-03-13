using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Data;
using System.Net;
using System.IO;
using System.Web;
using NCalc;
using ChatSharp;
using ChatSharp.Events;

namespace IRC_CarpBot {
	class Program {
		static object Lck = new object();
		static bool Running;
		static string Prmpt;
		static IrcChannel CurrentChannel;
		static IrcUser CurrentUser;
		static string[] Admins;
		public static IrcClient Client;

		static string Prompt(string P) {
			Console.Write(P);
			return Console.ReadLine().Trim();
		}

		static void Main(string[] args) {
			Console.Title = "Carp Bot";
			Console.WriteLine("Running Carp Bot");
			Admins = new string[] { "carpmanium", "mijyuoon", "mijyuoon-wp" };

			Running = true;

			bool RunInput = true;
			if (args.Length > 0 && args[0] == "-noinput")
				RunInput = false;

			Client = new IrcClient(
#if DEBUG
				"51.254.129.74"
#else
				"localhost"
#endif
				, new IrcUser("Carp_Bot", "Carp_Bot"));
			Client.ConnectionComplete += (S, E) => Client.JoinChannel("#main");
			Client.PrivateMessageRecieved += MessageReceived;
			//Client.ChannelMessageRecieved += MessageReceived;
			Client.UserJoinedChannel += (S, E) => MsgQueue.DequeueTo(E.User.Nick);
			Client.NetworkError += (S, E) => Running = false;

			if (RunInput) {
				Thread BGThread = new Thread(() => {
					Prmpt = "CarpBot";
					while (Running)
						ParseInput(Prompt(Prmpt + " $ "), null, null);
					//Command(Prompt(Prmpt + " $ "), null, null);
				});
				BGThread.Start();
			}

			Client.ConnectAsync();
			while (Running)
				;
			Client.Quit("Terminated");
			Thread.Sleep(500);
			Environment.Exit(0);
		}

		static void MessageReceived(object S, PrivateMessageEventArgs E) {
			IrcChannel Ch = null;
			if (Client.Channels.Contains(E.PrivateMessage.Source))
				Ch = Client.Channels[E.PrivateMessage.Source];
			IrcUser User = E.PrivateMessage.User;
			string Msg = E.PrivateMessage.Message;
			ParseInput(Msg, Ch, User);
		}

		static void ParseInput(string Msg, IrcChannel Ch, IrcUser User) {
			MatchCollection Matches = Regex.Matches(Msg,
					@"((www\.|https?):((//)|(\\\\))+[\w\d:#@%/;$()~_?\+-=\\\.&]*)");
			foreach (Match M in Matches) {
				string PageTitle = Utils.FetchTitle(M.Value).Trim();
				if (PageTitle.Length > 0)
					Command("say Page title: " + PageTitle, Ch, User);
			}

			if (Msg.StartsWith("!"))
				Command(Msg.Substring(1).Trim(), Ch, User);
		}

		public static void Command(string CmdStr, IrcChannel Ch, IrcUser Usr) {
			lock (Lck) {

				if (string.IsNullOrEmpty(CmdStr))
					return;
				string[] Cmd = CmdStr.Split(' ');

				string Executor = "";
				if (Usr != null)
					Executor = Usr.Nick;
				CurrentChannel = Ch;
				CurrentUser = Usr;

				if (Cmd[0] == "quit") {
					if (Usr == null || (Usr != null && Admins.Contains(Usr.Nick.ToLower()))) {
						if (Usr != null)
							Console.WriteLine("Quit by {0}", Usr.Nick);
						Running = false;
					} else
						WriteLine("{0}: You don't have enough privileges", Executor);
				} else if (Cmd[0] == "say") {
					WriteLine(CmdStr.Substring(4));
				} else if (Cmd[0] == "join") {
					string ChannelName = CmdStr.Substring(5).Trim();
					Client.JoinChannel(ChannelName);
					WriteLine("Joined channel {0}", ChannelName);
				} else if (Cmd[0] == "msg" && Cmd.Length > 2) {
					WriteLine("Queued message for {0}", Cmd[1]);
					MsgQueue.Enqueue(Cmd[1], Executor + ": " + CmdStr.Substring(Cmd[0].Length + Cmd[1].Length + 2));
				} else if ((Cmd[0] == "compute" || Cmd[0] == "c") && Cmd.Length > 1) {
					string Expr = CmdStr.Substring(Cmd[0].Length + 1);
					string Result = "NULL";
					try {
						Expression NCalcExpr = new Expression(Expr);
						NCalcExpr.EvaluateFunction += Utils.EvalFunc;
						NCalcExpr.EvaluateParameter += Utils.EvalParam;
						Result = NCalcExpr.Evaluate().ToString();
					} catch (Exception E) {
						Result = E.Message;
					}
					WriteLine("{0} = {1}", Expr, Result);
				} else
					WriteLine("{0}: Unknown command '{1}'", Executor, CmdStr);

			}
		}

		static void Write(string Msg) {
			Msg = Msg.Replace("\r", "");

			if (CurrentChannel != null || CurrentUser != null) {
				Msg = Msg.Trim();
				string[] Msgs = Msg.Split('\n');
				for (int i = 0; i < Msgs.Length; i++) {
					if (CurrentChannel != null)
						CurrentChannel.SendMessage(Msgs[i]);
					else if (CurrentUser != null)
						Client.SendMessage(Msgs[i], CurrentUser.Nick);
				}
			} else
				Console.Write(Msg);
		}

		static void Write(string Fmt, params object[] Args) {
			Write(string.Format(Fmt, Args));
		}

		static void WriteLine(string Msg) {
			Write(Msg + "\n");
		}

		static void WriteLine(string Fmt, params object[] Args) {
			WriteLine(string.Format(Fmt, Args));
		}
	}

	static class MsgQueue {
		const string FileName = "MsgQueue";
		static Dictionary<string, List<string>> TehQueue;

		static MsgQueue() {
			TehQueue = new Dictionary<string, List<string>>();
			Load();

			/*Thread MsgWorker = new Thread(() => {
				while (true) {
					Thread.Sleep(1000 * 60);
					foreach (var KV in TehQueue)
						DequeueTo(KV.Key);
				}
			});
			MsgWorker.IsBackground = true;
			MsgWorker.Start();*/
		}

		static void Save() {
			try {
				BinaryFormatter BF = new BinaryFormatter();
				FileStream FS = new FileStream(FileName, FileMode.Create);
				BF.Serialize(FS, TehQueue);
				FS.Flush();
				FS.Close();
			} catch (Exception) {
			}
		}

		static void Load() {
			try {
				BinaryFormatter BF = new BinaryFormatter();
				FileStream FS = new FileStream(FileName, FileMode.Open);
				TehQueue = (Dictionary<string, List<string>>)BF.Deserialize(FS);
				FS.Close();
			} catch (Exception) {
			}
		}

		public static void Enqueue(string Username, string Message) {
			if (!TehQueue.ContainsKey(Username))
				TehQueue.Add(Username, new List<string>());
			TehQueue[Username].Add(Message);
			DequeueTo(Username);
			Save();
		}

		public static string[] Dequeue(string Username) {
			if (TehQueue.ContainsKey(Username)) {
				string[] Ret = TehQueue[Username].ToArray();
				TehQueue[Username].Clear();
				Save();
				return Ret;
			}
			return new string[] { };
		}

		public static void DequeueTo(string Nick) {
			if (Program.Client.Users.Contains(Nick)) {
				string[] Msgs = MsgQueue.Dequeue(Nick);
				foreach (var M in Msgs)
					Program.Command("say " + M, null, Program.Client.Users[Nick]);
			}
		}
	}

	static class Utils {
		public static string FetchTitle(string Link) {
			try {
				WebRequest WReq = HttpWebRequest.Create(Link);
				if (WReq != null) {
					HttpWebResponse Resp = WReq.GetResponse() as HttpWebResponse;
					if (Resp != null)
						if (AllowTitleForType(Resp.ContentType)) {
							WebClient WC = new WebClient();
							string Str = Regex.Match(WC.DownloadString(Link), @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>",
								RegexOptions.IgnoreCase).Groups["Title"].Value;
							WC.Dispose();
							return HttpUtility.HtmlDecode(Str);
						} else
							return "";
				}
			} catch (Exception) {
				return "";
			}
			return "";
		}

		public static void EvalFunc(string Name, FunctionArgs Args) {
			if (Name == "ToStringit ") {
				object[] Params = Args.EvaluateParameters();
				Args.Result = string.Join("", Params);
			}
		}

		public static void EvalParam(string Name, ParameterArgs Args) {
			if (Name == "Pi")
				Args.Result = Math.PI;
			else if (Name == "E")
				Args.Result = Math.E;
			else if (Name == "Horo")
				Args.Result = "<3";
		}

		public static bool AllowTitleForType(string ContentType) {
			if (ContentType.StartsWith("text/html"))
				return true;
			return false;
		}
	}
}