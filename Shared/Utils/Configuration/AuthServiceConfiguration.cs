using System.Text;

namespace LaciSynchroni.Shared.Utils.Configuration;

public class AuthServiceConfiguration : LaciConfigurationBase
{
    public string GeoIPDbCityFile { get; set; } = string.Empty;
    /**
     * If you want to use GeoIP, you also have to download a GeoIP file.
     * You can get a compatible file here: https://github.com/P3TERX/GeoLite.mmdb?tab=readme-ov-file
     * Use the City.mmdb and configure it in GeoIPDbCityFile!
     */
    public bool UseGeoIP { get; set; } = false;
    public int FailedAuthForTempBan { get; set; } = 5;
    public int TempBanDurationInMinutes { get; set; } = 5;
    public List<string> WhitelistedIps { get; set; } = new();
    public Uri PublicOAuthBaseUri { get; set; } = null;
    public string? DiscordOAuthClientSecret { get; set; } = null;
    public string? DiscordOAuthClientId { get; set; } = null;
    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine(base.ToString());
        sb.AppendLine($"{nameof(RedisPool)} => {RedisPool}");
        sb.AppendLine($"{nameof(GeoIPDbCityFile)} => {GeoIPDbCityFile}");
        sb.AppendLine($"{nameof(UseGeoIP)} => {UseGeoIP}");
        return sb.ToString();
    }
}
