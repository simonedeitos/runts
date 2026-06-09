namespace EasySearch.Models;

public sealed class EnteStatistiche
{
    public int TotaleEnti { get; set; }
    public int Elaborati { get; set; }
    public int SitiTrovati { get; set; }
    public int EmailTrovate { get; set; }
    public int PecTrovate { get; set; }
    public int Errori { get; set; }
    public int PercentualeCompletamento => TotaleEnti == 0 ? 0 : (int)Math.Round((double)Elaborati / TotaleEnti * 100);
}
