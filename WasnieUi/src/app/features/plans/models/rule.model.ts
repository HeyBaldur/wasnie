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

/**
 * A transaction attribute a rule trigger can filter on, as declared by the ENGINE.
 *
 * Deliberately not a hardcoded list here: the field picker used to be a free-text box, so a name the
 * engine could not resolve saved fine and then never matched. `operators` carries only what that
 * field's evaluator genuinely implements, and `usesSet` marks the ones that read a list (In/NotIn).
 */
export interface TriggerField {
  field: string;
  valueType: 'String' | 'Number' | 'Boolean' | 'Date';
  operators: { operator: string; usesSet: boolean }[];
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

// ── Rule simulation ─────────────────────────────────────────────────────────
//
// ★ THE REQUEST CARRIES THE DEFINITION, NOT AN ID. The Live Preview card mirrors the FORM, and that
// form creates rules as well as edits them: by id there would be nothing to simulate while creating,
// and while editing the card would show the rate just typed beside a figure computed from the rate
// still in the database. Two contradictory numbers in one card is the exact loss of trust the card
// exists to prevent.
export interface SimulateRuleRequest extends AddRuleRequest {
  amount: number;
  quantity: number;
  // ★ Optional, and its absence is meaningful — omitting it makes the server REFUSE rather than fall
  // back on its 1.0 default, which would report a rep at full quota as if it were anybody.
  attainmentPct?: number | null;
  priorCumulative?: number | null;
  quotaTarget?: number | null;
}

// ★★ STRING ENUMS, BECAUSE THE API SENDS NAMES. `Program.cs` registers a JsonStringEnumConverter,
// so these arrive as "Cap" and "AppliedWithoutEffect", never as 4 and 2. Declared numeric, every
// comparison in the template would silently be false — no error, no warning, just a breakdown that
// renders the wrong rows. Caught by an integration test against the real endpoint, not by a unit
// test whose fixtures were hand-written to the shape they were assumed to have.

/** Never rendered raw — each value maps to an i18n key. */
export enum RuleSimulationBlocker {
  None = 'None',
  AttainmentContextRequired = 'AttainmentContextRequired',
  SplitQuotaContextRequired = 'SplitQuotaContextRequired',
}

export enum RuleCalculationComponent {
  Trigger = 'Trigger',
  Base = 'Base',
  Rate = 'Rate',
  Modifier = 'Modifier',
  Cap = 'Cap',
  Floor = 'Floor',
}

export enum RuleCalculationOutcome {
  NotConfigured = 'NotConfigured',
  Applied = 'Applied',
  AppliedWithoutEffect = 'AppliedWithoutEffect',
  Skipped = 'Skipped',
  NotMatched = 'NotMatched',
}

export enum AttainmentSource {
  Measured = 'Measured',
  Supplied = 'Supplied',
  Defaulted = 'Defaulted',
}

export interface RuleSimulationTier {
  from: number;
  to: number | null;
  rate: number;
  portion: number;
  amount: number;
}

export interface RuleSimulationStep {
  component: RuleCalculationComponent;
  outcome: RuleCalculationOutcome;
  inputAmount: number | null;
  outputAmount: number | null;
  operand: number | null;
  thresholdAmount: number | null;
  rateTable: RateTableType | null;
  attainmentSource: AttainmentSource | null;
  tiers: RuleSimulationTier[] | null;
}

export interface RuleSimulation {
  simulated: boolean;
  blocker: RuleSimulationBlocker;
  creditGenerated: boolean;
  commissionAmount: number | null;
  currency: string;
  steps: RuleSimulationStep[];
}
