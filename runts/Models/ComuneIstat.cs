namespace runts.Models;

/// <summary>
/// Rappresenta un comune ISTAT importato da CSV ufficiale.
/// </summary>
public sealed class ComuneIstat
{
    public string CodiceComune { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string SiglaProvincia { get; set; } = string.Empty;
    public string Regione { get; set; } = string.Empty;

    public override string ToString() => $"{Nome} ({SiglaProvincia}) - {Regione}";
}
