namespace Plumy.Application.Common;

/// <summary>
/// Thrown when the firm's plan doesn't cover what was asked: the trial ended, the month's invoices are used up or
/// the firm stopped paying. The API answers 402, and the screen offers the plans.
/// </summary>
public class PaymentRequiredException(string message) : Exception(message);
