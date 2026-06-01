using MediatR;
using Wasnie.Application.Compensation.DTOs;
using Wasnie.Domain.Common.Results;

namespace Wasnie.Application.Compensation.Commands.Payees;

public sealed record CreatePayeeCommand(
    string FullName,
    string EmployeeCode,
    string? Email,
    DateOnly? HireDate,
    string? Role = null,
    Guid? ManagerId = null) : IRequest<Result<PayeeDto>>;
