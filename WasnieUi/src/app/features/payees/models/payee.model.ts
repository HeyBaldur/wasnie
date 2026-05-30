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
  email: string;
  role: string | null;
  managerId: string | null;
  managerName: string | null;
  managerEmployeeCode: string | null;
  hireDate: string;
  terminationDate: string | null;
  status: PayeeStatus;
  statusLabel: string;
  activeAssignmentCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreatePayeeRequest {
  fullName: string;
  employeeCode: string;
  email: string;
  hireDate: string;
  role?: string | null;
  managerId?: string | null;
}

export interface UpdatePayeeRequest {
  payeeId: string;
  fullName: string;
  employeeCode: string;
  email: string;
  hireDate: string;
  role?: string | null;
  managerId?: string | null;
}

export interface PayeeListParams {
  page: number;
  pageSize: number;
  search: string;
  status: PayeeStatus | null;
}
