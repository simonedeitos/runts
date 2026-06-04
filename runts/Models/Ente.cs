namespace runts.Models;

public sealed class Ente
{
    public string Regione { get; set; } = string.Empty;
    public string Provincia { get; set; } = string.Empty;
    public string Comune { get; set; } = string.Empty;
    public string Denominazione { get; set; } = string.Empty;
    public string CodiceFiscale { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string SitoWeb { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PEC { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Stato { get; set; } = StatoEnte.DA_ELABORARE;
    public DateTime? DataUltimoControllo { get; set; }
}

public static class StatoEnte
{
    public const string DA_ELABORARE = nameof(DA_ELABORARE);
    public const string SITO_TROVATO = nameof(SITO_TROVATO);
    public const string EMAIL_TROVATA = nameof(EMAIL_TROVATA);
    public const string COMPLETATO = nameof(COMPLETATO);
    public const string ERRORE = nameof(ERRORE);
}
