using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace ForeverLauncher;

public static class Servers
{
	public const string FileName = "servers.json";

	public static readonly List<ServerEntry> List = new List<ServerEntry>();

	public static readonly List<string> Errors = new List<string>();

	public static string FilePath { get; private set; }

	private static string PortablePath => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "servers.json");

	private static string UserPath => Path.Combine(Settings.Dir, "servers.json");

	public static void Load()
	{
		List.Clear();
		Errors.Clear();
		FilePath = (File.Exists(PortablePath) ? PortablePath : UserPath);
		if (!File.Exists(FilePath))
		{
			WriteExample(FilePath);
		}
		object[] array;
		try
		{
			array = ((new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) is Dictionary<string, object> dictionary && dictionary.TryGetValue("servers", out var value) && value is IEnumerable) ? ((IEnumerable)value).Cast<object>().ToArray() : new object[0]);
		}
		catch (Exception ex)
		{
			Errors.Add(ex.Message);
			return;
		}
		object[] array2 = array;
		foreach (object obj in array2)
		{
			if (!(obj is Dictionary<string, object> dictionary2))
			{
				continue;
			}
			ServerEntry serverEntry = new ServerEntry();
			serverEntry.Name = Str(dictionary2, "name") ?? "";
			serverEntry.Portal = (Str(dictionary2, "portal") ?? "").Trim();
			serverEntry.PublicKey = (Str(dictionary2, "publicKey") ?? "02596F0D0C061A8B30745988FD72C59E29EC367FB0F341F28E0F08D037BAFC69").Trim().ToUpperInvariant();
			serverEntry.RegionGroup = Int(dictionary2, "regionGroup", 8);
			serverEntry.LoginPort = Int(dictionary2, "loginPort", 1119);
			serverEntry.WorldPort = Int(dictionary2, "worldPort", 8085);
			serverEntry.StatusUrl = Str(dictionary2, "statusUrl") ?? "";
			serverEntry.Website = Str(dictionary2, "website") ?? "";
			serverEntry.SupportHost = (Str(dictionary2, "supportHost") ?? "").Trim();
			serverEntry.Address = (Str(dictionary2, "address") ?? "").Trim();
			serverEntry.CaFile = (Str(dictionary2, "caFile") ?? "").Trim();
			serverEntry.BlockServices = !dictionary2.TryGetValue("blockServices", out var value3) || !(value3 is bool) || (bool)value3;
			ServerEntry serverEntry2 = serverEntry;
			if (dictionary2.TryGetValue("realms", out var value2) && value2 is IEnumerable && !(value2 is string))
			{
				foreach (object item in (IEnumerable)value2)
				{
					Dictionary<string, object> dictionary3 = item as Dictionary<string, object>;
					int num = ((dictionary3 != null) ? Int(dictionary3, "port", 0) : 0);
					if (num > 0 && num < 65536)
					{
						serverEntry2.Realms.Add(new RealmEntry
						{
							Name = (Str(dictionary3, "name") ?? ("Port " + num)),
							Port = num,
							Kind = (Str(dictionary3, "kind") ?? "").ToLowerInvariant()
						});
					}
				}
			}
			if (serverEntry2.Realms.Count == 0)
			{
				serverEntry2.Realms = ServerEntry.DefaultRealms(serverEntry2.WorldPort);
			}
			string text = serverEntry2.Validate();
			if (text != null)
			{
				Errors.Add(((serverEntry2.Name.Length > 0) ? serverEntry2.Name : ("server #" + (List.Count + Errors.Count + 1))) + ": " + text);
			}
			else
			{
				List.Add(serverEntry2);
			}
		}
	}

	public static ServerEntry Find(string name)
	{
		return List.FirstOrDefault((ServerEntry s) => s.Name == name) ?? List.FirstOrDefault();
	}

	private static void WriteExample(string path)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			string[] lines = new string[18]
			{
				"{",
				"  \"servers\": [",
				"    {",
				"      \"name\": \"My server\",",
				"      \"portal\": \"myserver.actual.battle.net:1119\",",
				"      \"address\": \"203.0.113.10\",",
				"      \"caFile\": \"myserver-ca.pem\",",
				"      \"blockServices\": true,",
				"      \"publicKey\": \"02596F0D0C061A8B30745988FD72C59E29EC367FB0F341F28E0F08D037BAFC69\",",
				"      \"regionGroup\": 8,",
				"      \"loginPort\": 1119,",
				"      \"worldPort\": 8085,",
				"      \"statusUrl\": \"\",",
				"      \"website\": \"\"",
				"    }",
				"  ]",
				"}",
				""
			};
			File.WriteAllText(path, string.Join(Environment.NewLine, lines), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
		}
	}

	private static string Str(Dictionary<string, object> d, string k)
	{
		if (!d.TryGetValue(k, out var value) || value == null)
		{
			return null;
		}
		return value.ToString();
	}

	private static int Int(Dictionary<string, object> d, string k, int def)
	{
		if (!d.TryGetValue(k, out var value) || value == null || !int.TryParse(value.ToString(), out var result))
		{
			return def;
		}
		return result;
	}
}
