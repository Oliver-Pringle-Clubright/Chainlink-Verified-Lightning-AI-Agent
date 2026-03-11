using LightningAgent.Api.DTOs;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Provides read access to payment records.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/payments")]
[Route("api/v{version:apiVersion}/payments")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentRepository paymentRepository,
        ILogger<PaymentsController> logger)
    {
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    /// <summary>
    /// List payments with optional task/agent filters and cursor-based pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<Payment>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaginatedResponse<Payment>>> ListPayments(
        [FromQuery] int? taskId,
        [FromQuery] int? agentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? cursor = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var offset = (page - 1) * pageSize;
        var totalCount = await _paymentRepository.GetFilteredCountAsync(taskId, agentId, ct);
        var payments = await _paymentRepository.GetFilteredPagedAsync(offset, pageSize, taskId, agentId, cursor, ct);
        var items = payments.ToList();

        // Compute the next cursor: the Id of the last item in this page (results are ORDER BY Id DESC)
        int? nextCursor = items.Count == pageSize && items.Count > 0
            ? items[^1].Id
            : null;

        return Ok(new PaginatedResponse<Payment>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            NextCursor = nextCursor
        });
    }

    /// <summary>
    /// Get a single payment by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(Payment), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Payment>> GetPayment(int id, CancellationToken ct)
    {
        var payment = await _paymentRepository.GetByIdAsync(id, ct);
        if (payment is null)
            return NotFound($"Payment {id} not found.");

        return Ok(payment);
    }
}
