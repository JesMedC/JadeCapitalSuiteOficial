import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { OllamaHealthInterval, AiProviderStatus, resolveAiStatus } from '../ollama-health.interval';
import { RiskAdviceService } from '@features/trader/risk-advice/api/risk-advice.service';
import { AiHealthDto } from '@features/trader/risk-advice/api/risk-advice.types';

// ============================================================================
//  Slice 5c.2 — Phase 1: ollama-health.interval
//
//  Service contract:
//    - status: 'up' | 'down' | 'unknown'  (initial = 'unknown')
//    - start()  — kicks off 60s polling
//    - stop()   — clears the timer
//    - pollNow() — single-shot probe
//
//  Pure-function coverage (resolveAiStatus) keeps the mapping logic
//  testable without fakeAsync + microtask flush semantics.
// ============================================================================

describe('resolveAiStatus (pure)', () => {
  it('maps a status=ok dto to "up"', () => {
    expect(resolveAiStatus({ status: 'ok', model: 'llama3.1' })).toBe<AiProviderStatus>('up');
  });

  it('maps a status=down dto to "down"', () => {
    expect(resolveAiStatus({ status: 'down', model: null })).toBe<AiProviderStatus>('down');
  });

  it('maps a null dto to "down" (defensive)', () => {
    expect(resolveAiStatus(null)).toBe<AiProviderStatus>('down');
  });

  it('maps a thrown error to "down"', () => {
    expect(resolveAiStatus(null, new Error('Network error'))).toBe<AiProviderStatus>('down');
  });

  it('prefers error over dto when both are present (defensive)', () => {
    expect(resolveAiStatus({ status: 'ok', model: 'x' }, new Error('late failure'))).toBe<AiProviderStatus>('down');
  });
});

describe('OllamaHealthInterval (orchestration)', () => {
  let getHealthMock: jest.Mock<Promise<AiHealthDto>, []>;

  beforeEach(async () => {
    getHealthMock = jest.fn();

    await TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: RiskAdviceService, useValue: { getHealth: getHealthMock } },
        OllamaHealthInterval,
      ],
    }).compileComponents();
  });

  function makeService(): OllamaHealthInterval {
    return TestBed.inject(OllamaHealthInterval);
  }

  it('exposes initial status as "unknown" before any poll', () => {
    const svc = makeService();
    expect(svc.status()).toBe<AiProviderStatus>('unknown');
  });

  it('pollNow resolves "up" when getHealth returns status=ok', (done) => {
    getHealthMock.mockResolvedValue({ status: 'ok', model: 'llama3.1' });
    const svc = makeService();
    void svc.pollNow().then(() => {
      expect(svc.status()).toBe<AiProviderStatus>('up');
      done();
    }).catch(done);
  });

  it('pollNow resolves "down" when getHealth returns status=down', (done) => {
    getHealthMock.mockResolvedValue({ status: 'down', model: null });
    const svc = makeService();
    void svc.pollNow().then(() => {
      expect(svc.status()).toBe<AiProviderStatus>('down');
      done();
    }).catch(done);
  });

  it('pollNow resolves "down" when getHealth throws (provider unreachable)', (done) => {
    const err = new Error('Network error');
    getHealthMock.mockImplementation(() => Promise.reject(err));
    const svc = makeService();
    void svc.pollNow().then(() => {
      expect(svc.status()).toBe<AiProviderStatus>('down');
      done();
    }).catch(done);
  });

  it('fires the poll on the configured interval (test seam @ 150ms)', (done) => {
    getHealthMock.mockResolvedValue({ status: 'ok', model: 'llama3.1' });
    const svc = makeService();
    svc.start(150);
    expect(getHealthMock).not.toHaveBeenCalled();

    // After ~150ms: first tick should fire.
    setTimeout(() => {
      expect(getHealthMock).toHaveBeenCalledTimes(1);
      // After ~300ms total: second tick should fire.
      setTimeout(() => {
        expect(getHealthMock).toHaveBeenCalledTimes(2);
        svc.stop();
        done();
      }, 200);
    }, 200);
  });

  it('stop() cancels further polls', (done) => {
    getHealthMock.mockResolvedValue({ status: 'ok', model: 'llama3.1' });
    const svc = makeService();
    svc.start(100);
    setTimeout(() => {
      expect(getHealthMock).toHaveBeenCalledTimes(1);
      svc.stop();
      setTimeout(() => {
        expect(getHealthMock).toHaveBeenCalledTimes(1); // no further calls
        done();
      }, 300);
    }, 150);
  });

  it('start() is idempotent — calling twice does not double-poll', (done) => {
    getHealthMock.mockResolvedValue({ status: 'ok', model: 'llama3.1' });
    const svc = makeService();
    svc.start(100);
    svc.start(100);
    svc.start(100);
    setTimeout(() => {
      expect(getHealthMock).toHaveBeenCalledTimes(1);
      svc.stop();
      done();
    }, 200);
  });
});
