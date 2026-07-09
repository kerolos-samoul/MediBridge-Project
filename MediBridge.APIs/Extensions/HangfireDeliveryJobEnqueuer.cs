using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MediBridge.APIs.Config;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Extensions;

public sealed class HangfireDeliveryJobEnqueuer : IDeliveryJobEnqueuer
{
    private readonly IBackgroundJobClient client;
    private readonly string queueName;

    public HangfireDeliveryJobEnqueuer(IBackgroundJobClient client, IOptions<DeliveryJobOptions> options)
    {
        this.client = client;
        queueName = options.Value.QueueName;
    }

    public Task<DeliveryJobEnqueueResult> EnqueueExpiryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = client.Create(
            Job.FromExpression<IDeliveryExpiryService>(service => service.RunAsync(CancellationToken.None)),
            new EnqueuedState(queueName));
        return Task.FromResult(new DeliveryJobEnqueueResult(id));
    }

    public Task<DeliveryJobEnqueueResult> EnqueueInjectorContinuationAsync(
        string expirySchedulerJobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expirySchedulerJobId);
        cancellationToken.ThrowIfCancellationRequested();
        var id = client.Create(
            Job.FromExpression<IDailyDeliveryInjectorService>(service => service.RunAsync(CancellationToken.None)),
            new AwaitingState(
                expirySchedulerJobId,
                new EnqueuedState(queueName),
                JobContinuationOptions.OnlyOnSucceededState));
        return Task.FromResult(new DeliveryJobEnqueueResult(id));
    }

    public Task<DeliveryJobEnqueueResult> EnqueueInjectorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = client.Create(
            Job.FromExpression<IDailyDeliveryInjectorService>(service => service.RunAsync(CancellationToken.None)),
            new EnqueuedState(queueName));
        return Task.FromResult(new DeliveryJobEnqueueResult(id));
    }
}
