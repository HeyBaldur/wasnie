export enum PayeeStatus {
  Active = 'Active',
  OnLeave = 'OnLeave',
  Terminated = 'Terminated',
}

export interface Payee {
  id: string;
  tenantId: string;
  fullName: string;
  employeeCode: string;
  email: string | null;
  role: string | null;
  managerId: string | null;
  managerName: string | null;
  managerEmployeeCode: string | null;
  hireDate: string | null;
  terminationDate: string | null;
  status: PayeeStatus;
  statusLabel: string;
  activeAssignmentCount: number;
  createdAt: string;
  updatedAt: string;
  employmentType?: string | null;
  location?: string | null;
  isActive: boolean;
  deactivatedAt?: string | null;
}

export interface CreatePayeeRequest {
  fullName: string;
  employeeCode: string;
  email: string | null;
  hireDate: string | null;
  role?: string | null;
  managerId?: string | null;
  employmentType?: string | null;
  location?: string | null;
}

export interface UpdatePayeeRequest {
  payeeId: string;
  fullName: string;
  employeeCode: string;
  email: string | null;
  hireDate: string | null;
  role?: string | null;
  managerId?: string | null;
  employmentType?: string | null;
  location?: string | null;
}

export interface PayeeListParams {
  page: number;
  pageSize: number;
  search: string;
  status: PayeeStatus | null;
}
