using LightningAgent.Api.DTOs;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/payments")]
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

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<Payment>>> ListPayments(
        [FromQuery] int? taskId,
        [FromQuery] int? agentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        // When filtered by taskId or agentId, use in-memory pagination
        if (taskId.HasValue)
        {
            var taskPayments = await _paymentRepository.GetByTaskIdAsync(taskId.Value, ct);
            var total = taskPayments.Count;
            var items = taskPayments.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(new PaginatedResponse<Payment>
            {
                Items = items, Page = page, PageSize = pageSize, TotalCount = total
            });
        }

        if (agentId.HasValue)
        {
            var agentPayments = await _paymentRepository.GetByAgentIdAsync(agentId.Value, ct);
            var total = agentPayments.Count;
            var items = agentPayments.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Ok(new PaginatedResponse<Payment>
            {
                Items = items, Page = page, PageSize = pageSize, TotalCount = total
            });
        }

        // No filter: return all payments, paginated
        var offset = (page - 1) * pageSize;
        var totalCount = await _paymentRepository.GetCountAsync(ct);
        var payments = await _paymentRepository.GetPagedAsync(offset, pageSize, ct);

        return Ok(new PaginatedResponse<Payment>
        {
            Items = payments.ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Payment>> GetPayment(int id, CancellationToken ct)
    {
        var payment = await _paymentRepository.GetByIdAsync(id, ct);
        if (payment is null)
            return NotFound($"Payment {id} not found.");

        return Ok(payment);
    }
}
