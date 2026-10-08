namespace Kassie.Makeover.Models;

public sealed record MakeupRecommendation(
    string Title,
    string Summary,
    string Why,
    MakeupSettings Settings);
