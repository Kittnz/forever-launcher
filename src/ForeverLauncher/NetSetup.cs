using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace ForeverLauncher;

// One-time, per-server network setup that replaces in-memory URL patching (which makes the
// build 70888 loader raise 0xC00000E5 about 30 s after start; see README).
//
//  * hosts file: "<server address> <portal>" when the server entry names an address
//    (remote server, portal under actual.battle.net), and optionally the Blizzard service
//    hostnames mapped to 127.0.0.1 so the client never reaches Blizzard (BlockServices).
//  * certificate: the server's CA is trusted in the Windows Root store. A CA is only
//    installed if it carries a NameConstraints extension, so it cannot vouch for other sites.
//
// Both need administrator rights, so the launcher re-runs itself elevated ("--net-setup")
// once, and does nothing at all when everything is already in place.
public static class NetSetup
{
	public const string Marker = "# ForeverLauncher";

	public const string Switch = "--net-setup";

	// Hostnames the 1.60.1 client contacts (from the executable's string table and observed traffic).
	public static readonly string[] ServiceHosts = new string[11]
	{
		"telemetry-in.battle.net",
		"wow.ingest.gdp.blizzard.com",
		"us.version.battle.net",
		"eu.version.battle.net",
		"kr.version.battle.net",
		"tw.version.battle.net",
		"xx.version.battle.net",
		"cn.version.battlenet.com.cn",
		"nydus.battle.net",
		"breaking-news.support.blizzard.com",
		"breaking-news.support.battlenet.com.cn"
	};

	private static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

	private static bool IsIpLiteral(string s)
	{
		IPAddress address;
		return IPAddress.TryParse(s, out address);
	}

	// "127.0.0.1" and "localhost" portals need no hosts line; "host:port" is split off.
	public static string PortalHost(ServerEntry server)
	{
		string text = server.Portal ?? "";
		int num = text.LastIndexOf(':');
		if (num > 0 && text.IndexOf(':') == num)
		{
			text = text.Substring(0, num);
		}
		return text;
	}

	public static string ResolveAddress(string address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return null;
		}
		IPAddress address2;
		if (IPAddress.TryParse(address, out address2))
		{
			return address2.ToString();
		}
		IPAddress ipAddress = Dns.GetHostAddresses(address).FirstOrDefault((IPAddress a) => a.AddressFamily == AddressFamily.InterNetwork);
		return ipAddress?.ToString();
	}

	// The hosts lines this server needs (without the marker).
	public static List<string> WantedLines(ServerEntry server, string resolvedAddress)
	{
		List<string> list = new List<string>();
		string text = PortalHost(server);
		if (resolvedAddress != null && text.Length > 0 && !IsIpLiteral(text))
		{
			list.Add(resolvedAddress + " " + text);
		}
		if (server.BlockServices)
		{
			foreach (string serviceHost in ServiceHosts)
			{
				list.Add("127.0.0.1 " + serviceHost);
			}
		}
		return list;
	}

	private static string Normalize(string line)
	{
		string text = line;
		int num = text.IndexOf('#');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		return string.Join(" ", text.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
	}

	public static bool HostsOk(List<string> wanted)
	{
		if (wanted.Count == 0)
		{
			return true;
		}
		HashSet<string> have;
		try
		{
			have = new HashSet<string>(File.ReadAllLines(HostsPath).Select(Normalize));
		}
		catch (IOException)
		{
			return false;
		}
		return wanted.All((string w) => have.Contains(w.ToLowerInvariant()));
	}

	public static string CaPath(ServerEntry server)
	{
		if (string.IsNullOrWhiteSpace(server.CaFile))
		{
			return null;
		}
		if (Path.IsPathRooted(server.CaFile))
		{
			return server.CaFile;
		}
		string text = (string.IsNullOrEmpty(Servers.FilePath) ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(Servers.FilePath));
		return Path.Combine(text, server.CaFile);
	}

	public static bool CaTrusted(string caPath)
	{
		if (caPath == null)
		{
			return true;
		}
		X509Certificate2 x509Certificate = new X509Certificate2(caPath);
		foreach (StoreLocation location in new StoreLocation[2] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
		{
			X509Store x509Store = new X509Store(StoreName.Root, location);
			try
			{
				x509Store.Open(OpenFlags.ReadOnly);
				if (x509Store.Certificates.Find(X509FindType.FindByThumbprint, x509Certificate.Thumbprint, validOnly: false).Count > 0)
				{
					return true;
				}
			}
			catch (Exception)
			{
			}
			finally
			{
				x509Store.Close();
			}
		}
		return false;
	}

	public static bool HasNameConstraints(X509Certificate2 ca)
	{
		return ca.Extensions.Cast<X509Extension>().Any((X509Extension e) => e.Oid != null && e.Oid.Value == "2.5.29.30");
	}

	// Called on the UI/patcher side. Returns null when everything is (now) in place, otherwise
	// an English problem description. Shows a single UAC prompt if something is missing.
	public static string Ensure(ServerEntry server, Action<string> log)
	{
		if (Array.IndexOf(Environment.GetCommandLineArgs(), "--net-setup") > 0)
		{
			return "refusing to start another elevated setup from inside a setup worker";
		}
		string address;
		try
		{
			address = ResolveAddress(server.Address);
		}
		catch (Exception ex)
		{
			return "could not resolve the server address \"" + server.Address + "\": " + ex.Message;
		}
		if (!string.IsNullOrWhiteSpace(server.Address) && address == null)
		{
			return "could not resolve the server address \"" + server.Address + "\"";
		}
		List<string> list = WantedLines(server, address);
		string text = CaPath(server);
		if (text != null && !File.Exists(text))
		{
			return "the CA file \"" + text + "\" was not found";
		}
		bool flag = HostsOk(list);
		bool flag2;
		try
		{
			flag2 = CaTrusted(text);
		}
		catch (Exception ex2)
		{
			return "the CA file \"" + text + "\" is not a valid certificate: " + ex2.Message;
		}
		if (flag && flag2)
		{
			return null;
		}
		log("network setup needed (hosts " + (flag ? "ok" : "missing") + ", certificate " + (flag2 ? "trusted" : "not trusted") + "); asking for administrator rights once");
		string arguments = "--net-setup h" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", flag ? new List<string>() : list))) + " c" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
		try
		{
			Process process = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, arguments)
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			});
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				return "the network setup failed (code " + process.ExitCode + "): " + Describe(process.ExitCode);
			}
		}
		catch (System.ComponentModel.Win32Exception)
		{
			return "the network setup needs administrator rights and the prompt was declined";
		}
		string text2 = null;
		if (!HostsOk(list))
		{
			text2 = "the hosts file still does not have the server lines (an antivirus may guard it)";
		}
		else if (!CaTrusted(text))
		{
			text2 = "the server certificate is still not trusted";
		}
		return text2;
	}

	private static string Describe(int code)
	{
		switch (code)
		{
		case 2:
			return "bad arguments";
		case 3:
			return "hosts file could not be written";
		case 4:
			return "the CA file is missing or invalid";
		case 5:
			return "the CA has no name constraints, refusing to trust it machine-wide";
		case 6:
			return "the certificate store refused the CA";
		default:
			return "unknown";
		}
	}

	// Elevated worker: "ForeverLauncher.exe --net-setup <b64 hosts lines> <b64 ca path>"
	private static void WorkerLog(string line)
	{
		try
		{
			File.AppendAllText(Path.Combine(Path.GetTempPath(), "foreverlauncher-setup.log"), DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine);
		}
		catch (Exception)
		{
		}
	}

	public static int ElevatedMain(string[] args)
	{
		WorkerLog("worker start, args=" + args.Length + ", admin=" + new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
		for (int i = 0; i < args.Length; i++)
		{
			WorkerLog("  arg[" + i + "] = " + (args[i].Length > 60 ? args[i].Substring(0, 60) + "..." : args[i]));
		}
		int code = ElevatedCore(args);
		WorkerLog("worker exit " + code);
		return code;
	}

	private static int ElevatedCore(string[] args)
	{
		int num = Array.IndexOf(args, "--net-setup");
		if (num < 0 || num + 2 >= args.Length)
		{
			return 2;
		}
		string[] array;
		string text;
		try
		{
			array = Encoding.UTF8.GetString(Convert.FromBase64String(args[num + 1].Substring(1))).Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			text = Encoding.UTF8.GetString(Convert.FromBase64String(args[num + 2].Substring(1)));
		}
		catch (FormatException)
		{
			return 2;
		}
		foreach (string s in array)
		{
			string[] array2 = s.Split(' ');
			IPAddress address;
			if (array2.Length != 2 || !IPAddress.TryParse(array2[0], out address) || array2[1].Any((char c) => !char.IsLetterOrDigit(c) && c != '.' && c != '-'))
			{
				return 2;
			}
		}
		if (text.Length > 0)
		{
			X509Certificate2 x509Certificate;
			try
			{
				x509Certificate = new X509Certificate2(text);
			}
			catch (Exception ex4)
			{
				WorkerLog("CA load failed: " + ex4.Message);
				return 4;
			}
			if (!HasNameConstraints(x509Certificate))
			{
				return 5;
			}
			X509Store x509Store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
			try
			{
				x509Store.Open(OpenFlags.ReadWrite);
				x509Store.Add(x509Certificate);
			}
			catch (Exception ex6)
			{
				WorkerLog("store add failed: " + ex6.Message);
				return 6;
			}
			finally
			{
				x509Store.Close();
			}
		}
		if (array.Length > 0)
		{
			if (!WriteHosts(array))
			{
				return 3;
			}
		}
		return 0;
	}

	private static bool HasOurLines()
	{
		try
		{
			return File.ReadAllLines(HostsPath).Any((string l) => l.Contains(Marker));
		}
		catch (IOException)
		{
			return false;
		}
	}

	// Replaces every line this launcher wrote earlier with the wanted ones. The hosts file is
	// normally system+hidden+read-only, so the attributes are cleared for the write and restored.
	private static bool WriteHosts(string[] wanted)
	{
		string hostsPath = HostsPath;
		FileAttributes attributes = FileAttributes.Normal;
		try
		{
			attributes = File.GetAttributes(hostsPath);
			string[] source = File.ReadAllLines(hostsPath);
			List<string> list = source.Where((string l) => !l.Contains(Marker)).ToList();
			if (!File.Exists(hostsPath + ".foreverlauncher.bak"))
			{
				File.Copy(hostsPath, hostsPath + ".foreverlauncher.bak");
			}
			list.AddRange(wanted.Select((string w) => w + " " + Marker));
			File.SetAttributes(hostsPath, FileAttributes.Normal);
			for (int i = 0; i < 3; i++)
			{
				try
				{
					File.WriteAllLines(hostsPath, list, new ASCIIEncoding());
					return true;
				}
				catch (IOException)
				{
					Thread.Sleep(1500);
				}
			}
			return false;
		}
		catch (Exception ex)
		{
			WorkerLog("hosts write failed: " + ex.GetType().Name + ": " + ex.Message);
			return false;
		}
		finally
		{
			try
			{
				File.SetAttributes(hostsPath, attributes);
			}
			catch (Exception)
			{
			}
		}
	}
}
