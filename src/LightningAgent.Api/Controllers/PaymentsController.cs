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
    public async Task<ActionResult<List<Payment>>> ListPayments(
        [FromQuery] int? taskId,
        [FromQuery] int? agentId,
        CancellationToken ct)
    {
        IReadOnlyList<Payment> payments;

        if (taskId.HasValue)
        {
            payments = await _paymentRepository.GetByTaskIdAsync(taskId.Value, ct);
        }
        else if (agentId.HasValue)
        {
            payments = await _paymentRepository.GetByAgentIdAsync(agentId.Value, ct);
        }
        else
        {
            return BadRequest("Provide either taskId or agentId query parameter.");
        }

        return Ok(payments);
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
