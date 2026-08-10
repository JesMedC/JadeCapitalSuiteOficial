import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface AccountDto {
  id: string;
  userId: string;
  name: string;
  broker: string;
  currency: string;
  initialBalance: number;
  leverage: number;
  payoutPercent: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class AccountApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/accounts';

  async list(): Promise<AccountDto[]> {
    return firstValueFrom(this.http.get<AccountDto[]>(this.base));
  }

  async getById(id: string): Promise<AccountDto> {
    return firstValueFrom(this.http.get<AccountDto>(`${this.base}/${id}`));
  }

  async create(req: Omit<AccountDto, 'id' | 'userId' | 'isActive' | 'createdAt' | 'updatedAt'>): Promise<AccountDto> {
    return firstValueFrom(this.http.post<AccountDto>(this.base, req));
  }

  async update(id: string, req: Pick<AccountDto, 'name' | 'broker' | 'currency' | 'leverage' | 'payoutPercent'>): Promise<AccountDto> {
    return firstValueFrom(this.http.patch<AccountDto>(`${this.base}/${id}`, req));
  }

  async deactivate(id: string): Promise<AccountDto> {
    return firstValueFrom(this.http.post<AccountDto>(`${this.base}/${id}/deactivate`, {}));
  }

  async reactivate(id: string): Promise<AccountDto> {
    return firstValueFrom(this.http.post<AccountDto>(`${this.base}/${id}/reactivate`, {}));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
  }
}
