import { TestBed } from '@angular/core/testing';
import { RiskProfileService } from '../api/risk-profile.service';
import { RiskProfileDto, UpsertRiskProfileRequest } from '../api/risk-profile.types';
import { RiskProfileState } from './risk-profile.state';

describe('RiskProfileState', () => {
  let state: RiskProfileState;
  let api: { getActive: jest.Mock; upsert: jest.Mock };

  beforeEach(() => {
    api = { getActive: jest.fn(), upsert: jest.fn() };
    TestBed.configureTestingModule({
      providers: [RiskProfileState, { provide: RiskProfileService, useValue: api }],
    });
    state = TestBed.inject(RiskProfileState);
  });

  it('load() sets the profile signal on a successful 200', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    await state.load();
    expect(state.profile()).toEqual(sampleDto());
    expect(state.isLoading()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('load() sets profile=null when the backend returns 404 (no profile yet)', async () => {
    api.getActive.mockResolvedValueOnce(null);
    await state.load();
    expect(state.profile()).toBeNull();
    expect(state.isEmpty()).toBe(true);
    expect(state.error()).toBeNull();
  });

  it('load() exposes the error when the service rejects (e.g. 500)', async () => {
    api.getActive.mockRejectedValueOnce({
      status: 500,
      code: 'unknown',
      message: 'boom',
    });
    await state.load();
    expect(state.profile()).toBeNull();
    expect(state.error()).toEqual({ status: 500, code: 'unknown', message: 'boom' });
  });

  it('save() updates the profile and flips saveSuccess on 200', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    await state.load();

    const updated = { ...sampleDto(), riskPerTradePercent: 2 };
    api.upsert.mockResolvedValueOnce(updated);

    const result = await state.save({
      capitalAmount: 10_000,
      capitalCurrency: 'USD',
      maxDrawdownPercent: 10,
      riskPerTradePercent: 2,
      riskRewardTarget: 2,
    });

    expect(result).toEqual(updated);
    expect(state.profile()).toEqual(updated);
    expect(state.saveSuccess()).toBe(true);
    expect(state.error()).toBeNull();
  });

  it('save() exposes the 422 error and does NOT clear the existing profile', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    await state.load();

    const err = {
      status: 422,
      code: 'validation.risk_profile.risk_per_trade_percent_out_of_range',
      message: 'Risk-per-trade percent must be between 0.01 and 5.00.',
      field: 'riskPerTradePercent',
    };
    api.upsert.mockRejectedValueOnce(err);

    const result = await state.save({
      capitalAmount: 10_000,
      capitalCurrency: 'USD',
      maxDrawdownPercent: 10,
      riskPerTradePercent: 7.5,
      riskRewardTarget: 2,
    });

    expect(result).toBeNull();
    expect(state.error()).toEqual(err);
    // Profile stays as the previous valid one so the user can correct the field.
    expect(state.profile()).toEqual(sampleDto());
    expect(state.fieldErrors()).toEqual({
      riskPerTradePercent: 'Risk-per-trade percent must be between 0.01 and 5.00.',
    });
  });

  it('save() exposes a 409 conflict and preserves the existing profile', async () => {
    api.getActive.mockResolvedValueOnce(sampleDto());
    await state.load();

    const err = {
      status: 409,
      code: 'conflict.risk_profile.concurrent_supersede',
      message: 'Another update is in progress.',
    };
    api.upsert.mockRejectedValueOnce(err);

    await state.save({
      capitalAmount: 10_000,
      capitalCurrency: 'USD',
      maxDrawdownPercent: 10,
      riskPerTradePercent: 1,
      riskRewardTarget: 2,
    });

    expect(state.error()).toEqual(err);
    expect(state.profile()).toEqual(sampleDto());
    expect(state.fieldErrors()).toEqual({});
  });

  it('clearError() resets the error signal', async () => {
    api.getActive.mockRejectedValueOnce({ status: 500, code: 'unknown', message: 'boom' });
    await state.load();
    expect(state.error()).not.toBeNull();
    state.clearError();
    expect(state.error()).toBeNull();
  });
});

function sampleDto(): RiskProfileDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    userId: '22222222-2222-2222-2222-222222222222',
    capitalAmount: 10_000,
    capitalCurrency: 'USD',
    maxDrawdownPercent: 10,
    riskPerTradePercent: 1,
    riskRewardTarget: 2,
    isActive: true,
    supersededAt: null,
    createdAt: '2026-08-15T10:00:00.000Z',
    updatedAt: '2026-08-15T10:00:00.000Z',
  };
}
