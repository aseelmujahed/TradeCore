using Microsoft.AspNetCore.Mvc;
using TradeCore.Api.DTOs.PortfolioTransfers;
using TradeCore.Console.Models;
using TradeCore.Console.Services;

namespace TradeCore.Api.Controllers;

[ApiController]
[Route("api/portfolio-transfers")]
public sealed class PortfolioTransfersController(PortfolioTransferService transferService, StockService stockService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PortfolioTransferResponse>> Create(CreatePortfolioTransferRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await transferService.RequestTransferAsync(request.AccountId, request.StockSymbol.Trim(), request.Quantity, request.AveragePrice, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = transfer.Id }, await ToResponseAsync(transfer, cancellationToken));
        }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        catch (KeyNotFoundException exception) { return BadRequest(exception.Message); }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortfolioTransferResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var transfers = await transferService.GetTransfersAsync(cancellationToken);
        var responses = await Task.WhenAll(transfers.Select(transfer => ToResponseAsync(transfer, cancellationToken)));
        return Ok(responses);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortfolioTransferResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await ToResponseAsync(await transferService.GetTransferAsync(id, cancellationToken), cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Simulates manual external verification; authorization must protect this endpoint when roles are introduced.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<PortfolioTransferResponse>> Complete(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await ToResponseAsync(await transferService.CompleteTransferAsync(id, cancellationToken), cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<PortfolioTransferResponse>> Reject(Guid id, CancellationToken cancellationToken)
    {
        try { return Ok(await ToResponseAsync(await transferService.RejectTransferAsync(id, cancellationToken), cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return BadRequest(exception.Message); }
    }

    private async Task<PortfolioTransferResponse> ToResponseAsync(PortfolioTransfer transfer, CancellationToken cancellationToken)
    {
        var stock = (await stockService.GetAllStocksAsync(cancellationToken))
            .Single(stock => stock.Id == transfer.StockId);
        return new PortfolioTransferResponse(transfer.Id, transfer.AccountId, transfer.StockId, stock.Symbol, transfer.Quantity, transfer.AveragePrice, transfer.Status, transfer.CreatedAt, transfer.CompletedAt);
    }
}
