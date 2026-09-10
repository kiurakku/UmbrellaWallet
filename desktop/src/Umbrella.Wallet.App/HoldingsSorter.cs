using System;
using System.Collections.Generic;
using System.Linq;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App;

/// <summary>
/// Orders the Holdings list for the portfolio. Pure and offline so it can be unit-tested, and stable
/// (LINQ OrderBy keeps the input order for ties), so an unknown / "Default" sort leaves the catalog order
/// exactly as it was.
/// </summary>
public static class HoldingsSorter
{
    public const string Default = "Default";
    public const string ByValue = "Value";
    public const string ByChange = "Change";
    public const string ByName = "Name";

    public static IEnumerable<HoldingRowViewModel> Order(IEnumerable<HoldingRowViewModel> rows, string? sort) =>
        sort switch
        {
            ByValue => rows.OrderByDescending(r => r.Value),                        // biggest holdings first
            ByChange => rows.OrderByDescending(r => r.Change24h),                   // best 24h movers first
            ByName => rows.OrderBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase),
            _ => rows,                                                              // Default: catalog order
        };
}
