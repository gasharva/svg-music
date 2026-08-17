namespace SvgStructure.Models;

/// <summary>
/// One continuous ledger-line ladder attached to a P+M logical block.
/// Depth is signed: negative values extend above the staff, positive values below it.
/// Its absolute value is the number of consecutive ledger lines, starting at the first
/// legal ledger level immediately outside the five-line staff.
/// </summary>
public sealed record LedgerLineResolution(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds,
    int Depth);
