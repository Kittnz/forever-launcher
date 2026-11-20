using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ForeverLauncher;

public sealed class ServerEntry
{
	public const string TrinityCorePublicKey = "02596F0D0C061A8B30745988FD72C59E29EC367FB0F341F28E0F08D037BAFC69";

	public string Name = "";

	public string Portal = "";

	public string PublicKey = "02596F0D0C061A8B30745988FD72C59E29EC367FB0F341F28E0F08D037BAFC69";

	public int RegionGroup = 8;

	public int LoginPort = 1119;

	public int WorldPort = 8085;

	public string StatusUrl = "";

	public string Website = "";

	public string SupportHost = "";

	// Remote server: its IP or host name. The portal name is mapped to it in the hosts file. Empty = portal resolves by itself (e.g. 127.0.0.1).
	public string Address = "";

	// PEM of the CA that signed the server's portal certificate (path relative to servers.json). Must be name-constrained.
	public string CaFile = "";

	// Map the Blizzard service hostnames to 127.0.0.1 (no telemetry/version/news traffic leaves the PC).
	public bool BlockServices = true;

	public List<RealmEntry> Realms = new List<RealmEntry>();

	public static List<RealmEntry> DefaultRealms(int worldPort)
	{
		List<RealmEntry> list = new List<RealmEntry>();
		list.Add(new RealmEntry
		{
			Name = "Normal",
			Port = worldPort,
			Kind = "normal"
		});
		list.Add(new RealmEntry
		{
			Name = "PvP",
			Port = worldPort + 10,
			Kind = "pvp"
		});
		list.Add(new RealmEntry
		{
			Name = "RP",
			Port = worldPort + 20,
			Kind = "rp"
		});
		list.Add(new RealmEntry
		{
			Name = "Hardcore",
			Port = worldPort + 30,
			Kind = "hardcore"
		});
		return list;
	}

	public string Validate()
	{
		if (string.IsNullOrWhiteSpace(Name))
		{
			return "missing \"name\"";
		}
		if (string.IsNullOrWhiteSpace(Portal) || Portal.Contains(" ") || Portal.Contains("/"))
		{
			return "\"portal\" must be a host name";
		}
		if (!Regex.IsMatch(PublicKey ?? "", "^[0-9A-Fa-f]{64}$"))
		{
			return "\"publicKey\" must be 64 hex characters";
		}
		if (RegionGroup < 2 || RegionGroup > 12 || RegionGroup == 9)
		{
			return "\"regionGroup\" must be 2-12 (not 9)";
		}
		if (!string.IsNullOrEmpty(Address) && !Regex.IsMatch(Address, "^[A-Za-z0-9.:-]+$"))
		{
			return "\"address\" must be an IP address or host name";
		}
		if (!string.IsNullOrEmpty(Address) && Portal.Split(':')[0].All((char c) => char.IsDigit(c) || c == '.'))
		{
			return "\"address\" is only used with a portal host name (e.g. myserver.actual.battle.net), not an IP address";
		}
		if (!string.IsNullOrEmpty(StatusUrl) && !StatusUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			return "\"statusUrl\" must start with https://";
		}
		if (!string.IsNullOrEmpty(SupportHost) && !Regex.IsMatch(SupportHost, "^[A-Za-z0-9.-]+$"))
		{
			return "\"supportHost\" must be a host name (no https://, port or path)";
		}
		if (!string.IsNullOrEmpty(SupportHost) && SupportHost.Length > Patcher.SupportHostMaxLength)
		{
			return "\"supportHost\" is too long (at most " + Patcher.SupportHostMaxLength + " characters)";
		}
		return null;
	}
}
