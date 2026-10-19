using SmartX.Core.Models;

namespace SmartX.Api.Services;

public sealed class TelemetryPipeline
{
    public const int StandardCapacity = 10_000;

    private readonly Queue<TelemetryMessage> _standard = new();
    private readonly PriorityQueue<TelemetryMessage, (int Rank, long Seq)> _priority = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();

    private long _sequence;
    private long _totalReceived;
    private long _rejected;
    private long _standardProcessed;
    private long _priorityProcessed;
    private double _standardLatencyTotal;
    private double _priorityLatencyTotal;
    private int _peakStandard;
    private int _peakPriority;

    public bool TryEnqueueStandard(TelemetryMessage message)
    {
        lock (_gate)
        {
            _totalReceived++;

            if (_standard.Count >= StandardCapacity)
            {
                _rejected++;
                return false;
            }

            _standard.Enqueue(message);
            if (_standard.Count > _peakStandard) _peakStandard = _standard.Count;
        }

        _signal.Release();
        return true;
    }

    public void EnqueuePriority(TelemetryMessage message, int rank)
    {
        lock (_gate)
        {
            _totalReceived++;
            _sequence++;
            _priority.Enqueue(message, (rank, _sequence));
            if (_priority.Count > _peakPriority) _peakPriority = _priority.Count;
        }

        _signal.Release();
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);

    public bool TryDequeue(out TelemetryMessage message, out bool fromPriority)
    {
        lock (_gate)
        {
            if (_priority.TryDequeue(out message, out _))
            {
                fromPriority = true;
                return true;
            }

            if (_standard.TryDequeue(out message))
            {
                fromPriority = false;
                return true;
            }
        }

        message = default;
        fromPriority = false;
        return false;
    }

    public void RecordProcessed(bool fromPriority, double latencyMs)
    {
        lock (_gate)
        {
            if (fromPriority)
            {
                _priorityProcessed++;
                _priorityLatencyTotal += latencyMs;
            }
            else
            {
                _standardProcessed++;
                _standardLatencyTotal += latencyMs;
            }
        }
    }

    public (int StdDepth, int PrioDepth, long StdProcessed, long PrioProcessed, int PeakStd, int PeakPrio,
        double StdAvgMs, double PrioAvgMs, long Total, long Rejected) Snapshot()
    {
        lock (_gate)
        {
            var stdAvg = _standardProcessed == 0 ? 0 : _standardLatencyTotal / _standardProcessed;
            var prioAvg = _priorityProcessed == 0 ? 0 : _priorityLatencyTotal / _priorityProcessed;

            return (_standard.Count, _priority.Count, _standardProcessed, _priorityProcessed,
                _peakStandard, _peakPriority, stdAvg, prioAvg, _totalReceived, _rejected);
        }
    }
}
