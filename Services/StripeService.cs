using Stripe;
using System;
using System.Net.Http;

public class StripeService
{
    private readonly StripeClient _client;

    public StripeService()
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:12111") // stripe-mock endpoint
        };

        _client = new StripeClient("sk_test_123", httpClient: new SystemNetHttpClient(httpClient));
    }

    public Customer CreateCustomer(string email)
    {
        var options = new CustomerCreateOptions
        {
            Email = email
        };

        var service = new CustomerService(_client);
        return service.Create(options);
    }

    public PaymentIntent CreatePaymentIntent(long amount, string currency, string customerId)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = currency,
            Customer = customerId,
            PaymentMethodTypes = new System.Collections.Generic.List<string> { "card" }
        };

        var service = new PaymentIntentService(_client);
        return service.Create(options);
    }
}
