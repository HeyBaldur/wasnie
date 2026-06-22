import { Money } from './value-objects.model';

export enum LogicalOperator {
  And = 0,
  Or = 1,
}

export enum ConditionOperator {
  Equal = 0,
  NotEqual = 1,
  GreaterThan = 2,
  GreaterThanOrEqual = 3,
  LessThan = 4,
  LessThanOrEqual = 5,
  In = 6,
  NotIn = 7,
}

export enum ConditionValueType {
  String = 0,
  Number = 1,
  Boolean = 2,
  Date = 3,
}

export enum MeasurementType {
  Revenue = 0,
  Units = 1,
  Margin = 2,
  Attainment = 3,
  Custom = 4,
}

export enum MeasurementAggregation {
  Sum = 0,
  Average = 1,
  Max = 2,
  Min = 3,
  Count = 4,
}

export enum RateTableType {
  Flat = 0,
  Tiered = 1,
  AttainmentBased = 2,
}

export enum ModifierType {
  Accelerator = 0,
  Multiplier = 1,
  Spiff = 2,
}

export enum CapScope {
  PerTransaction = 0,
  PerPeriod = 1,
  PerPayeePerPeriod = 2,
}

export interface ConditionValue {
  type: ConditionValueType;
  raw: string;
  set: string[] | null;
}

export interface Condition {
  field: string;
  operator: ConditionOperator;
  value: ConditionValue;
}

export interface Trigger {
  _schema: 1;
  logicalOperator: LogicalOperator;
  conditions: Condition[];
}

export interface Measurement {
  _schema: 1;
  type: MeasurementType;
  sourceField: string;
  aggregation: MeasurementAggregation;
}

export interface RateTier {
  from: number;
  to: number | null;
  rate: number;
}

export interface AttainmentTier {
  attainmentFrom: number;
  attainmentTo: number | null;
  rate: number;
}

export interface RateTable {
  _schema: 1;
  type: RateTableType;
  flatRate: number | null;
  tiers: RateTier[] | null;
  attainmentTiers: AttainmentTier[] | null;
  splitAtQuota: boolean;
}

export interface Modifier {
  _schema: 1;
  id: string;
  name: string;
  type: ModifierType;
  factor: number;
  trigger: Trigger | null;
}

export interface Cap {
  _schema: 1;
  amount: Money;
  scope: CapScope;
}

export interface Floor {
  _schema: 1;
  amount: Money;
}

export interface Rule {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  trigger: Trigger | null;
  measurement: Measurement;
  rateTable: RateTable;
  modifier: Modifier | null;
  cap: Cap | null;
  floor: Floor | null;
}

export interface AddRuleRequest {
  planId: string;
  name: string;
  sortOrder: number;
  measurement: Measurement;
  rateTable: RateTable;
  trigger: Trigger | null;
  modifier: Modifier | null;
  cap: Cap | null;
  floor: Floor | null;
}

export interface UpdateRuleRequest extends AddRuleRequest {
  ruleId: string;
}
