import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { TranslateModule, TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { getPlanPermissions } from '../services/plan-permissions';
import {
  CapScope,
  ConditionOperator,
  ConditionValueType,
  LogicalOperator,
  MeasurementAggregation,
  MeasurementType,
  ModifierType,
  RateTableType,
} from '../models/rule.model';
import { AddRuleRequest, UpdateRuleRequest } from '../models/rule.model';
import {
  WsPageHeaderComponent,
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';
import { WsTooltipDirective } from '../../../shared/ui/ws-tooltip/ws-tooltip.directive';

@Component({
  selector: 'app-rule-form',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    TranslatePipe,
    DecimalPipe,
    LowerCasePipe,
    WsPageHeaderComponent,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsTooltipDirective,
  ],
  templateUrl: './rule-form.component.html',
  styleUrl: './rule-form.component.scss',
})
export class RuleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);

  private readonly destroyRef = inject(DestroyRef);

  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly ruleId = this.route.snapshot.paramMap.get('ruleId') ?? null;
  readonly isEdit = !!this.ruleId;
  readonly saving = signal(false);
  readonly readOnly = signal(false);

  readonly MeasurementType = MeasurementType;
  readonly MeasurementAggregation = MeasurementAggregation;
  readonly RateTableType = RateTableType;
  readonly ModifierType = ModifierType;
  readonly CapScope = CapScope;
  readonly LogicalOperator = LogicalOperator;
  readonly ConditionOperator = ConditionOperator;
  readonly ConditionValueType = ConditionValueType;

  // V1: only Revenue and Units are supported. Margin, Attainment, and Custom
  // require additional transaction fields — activate in a future WI.
  // FILTER MUST apply to every MeasurementType picker surface; see 14-forbidden-patterns.md.
  readonly measurementTypeOptions: SelectOption[] = [
    { label: 'PLANS.MEASUREMENT_REVENUE', value: MeasurementType.Revenue },
    { label: 'PLANS.MEASUREMENT_UNITS', value: MeasurementType.Units },
  ];

  // Aggregation is not yet implemented in the motor — Sum is the only valid option.
  // Average/Max/Min/Count are hidden from the UI to prevent silent misconfiguration.
  readonly aggregationOptions: SelectOption[] = [
    { label: 'PLANS.AGGREGATION_SUM', value: MeasurementAggregation.Sum },
  ];

  /** True when Measurement Type = Units is selected. Drives conditional UI rendering. */
  readonly isUnitsMode = computed(() =>
    Number(this.formValue()?.measurement?.type ?? MeasurementType.Revenue) === MeasurementType.Units
  );

  readonly rateTableTypes = Object.entries(RateTableType)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.RATE_TABLE_${k.toUpperCase()}`, value: v as number }));

  readonly modifierTypeOptions: SelectOption[] = Object.entries(ModifierType)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.MODIFIER_${k.toUpperCase()}`, value: v as number }));

  readonly capScopeOptions: SelectOption[] = Object.entries(CapScope)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.CAP_SCOPE_${k.toUpperCase()}`, value: v as number }));

  readonly conditionOperatorOptions: SelectOption[] = Object.entries(ConditionOperator)
    .filter(([, v]) => typeof v === 'number')
    .map(([k, v]) => ({ label: `PLANS.COND_OP_${k.toUpperCase()}`, value: v as number }));

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(120)]],
    sortOrder: [1, [Validators.required, Validators.min(1)]],
    measurement: this.fb.nonNullable.group({
      type: [MeasurementType.Revenue as number, Validators.required],
      sourceField: ['amount', Validators.required],
      aggregation: [MeasurementAggregation.Sum as number, Validators.required],
    }),
    rateTable: this.fb.nonNullable.group({
      type: [RateTableType.Flat as number, Validators.required],
      flatRate: [0.05],
      tiers: this.fb.array<FormGroup>([]),
      attainmentTiers: this.fb.array<FormGroup>([]),
      splitAtQuota: [false],
    }),
    hasTrigger: [false],
    trigger: this.fb.nonNullable.group({
      logicalOperator: [LogicalOperator.And as number],
      conditions: this.fb.array<FormGroup>([]),
    }),
    hasModifier: [false],
    modifier: this.fb.nonNullable.group({
      name: [''],
      type: [ModifierType.Accelerator as number],
      factor: [1.5],
    }),
    hasCap: [false],
    cap: this.fb.nonNullable.group({
      amount: [0, Validators.min(0)],
      scope: [CapScope.PerPeriod as number],
    }),
    hasFloor: [false],
    floor: this.fb.nonNullable.group({
      amount: [0, Validators.min(0)],
    }),
  });

  readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  readonly rateTableType = computed(() => {
    const v = this.formValue();
    return Number(v.rateTable?.type ?? RateTableType.Flat) as RateTableType;
  });

  readonly hasTrigger = computed(() => !!this.formValue()?.hasTrigger);
  readonly hasModifier = computed(() => !!this.formValue()?.hasModifier);
  readonly hasCap = computed(() => !!this.formValue()?.hasCap);
  readonly hasFloor = computed(() => !!this.formValue()?.hasFloor);

  get tiersArray(): FormArray {
    return this.form.get('rateTable.tiers') as FormArray;
  }

  get attainmentTiersArray(): FormArray {
    return this.form.get('rateTable.attainmentTiers') as FormArray;
  }

  get conditionsArray(): FormArray {
    return this.form.get('trigger.conditions') as FormArray;
  }

  ngOnInit(): void {
    // When the user switches to Units mode, force rate table to Flat (only supported combination).
    // Also ensure hidden fields stay at their defaults.
    this.form.controls.measurement.controls.type.valueChanges.pipe(
      distinctUntilChanged(),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(type => {
      if (Number(type) === MeasurementType.Units) {
        this.form.controls.rateTable.controls.type.setValue(RateTableType.Flat as number, { emitEvent: false });
      }
      // sourceField and aggregation are hidden; keep at defaults regardless of type.
      this.form.controls.measurement.controls.sourceField.setValue('amount', { emitEvent: false });
      this.form.controls.measurement.controls.aggregation.setValue(MeasurementAggregation.Sum as number, { emitEvent: false });
    });

    const loadPromise = (!this.store.selectedPlan() || this.store.selectedPlan()?.id !== this.planId)
      ? this.store.loadPlan(this.planId)
      : Promise.resolve();

    loadPromise.then(() => {
      if (this.isEdit) {
        this._loadExistingRule();  // populate signals while form is still enabled
      } else {
        this._addTier();
      }

      const perms = getPlanPermissions(this.store.selectedPlan()?.status);
      if (!perms.canEditRule) {
        this.form.disable({ emitEvent: false });  // disable after load, no extra emission
        this.readOnly.set(true);
      }
    });
  }

  /**
   * The API uses JsonStringEnumConverter, so enum values arrive as string names
   * (e.g. "Revenue", "Flat"). The form options use numeric values (0, 1, 2…).
   * This helper coerces either representation to the numeric value so that
   * WsSelect's `selectedOption` comparison (===) finds the correct option.
   */
  private _enumToNumber<T extends Record<string, unknown>>(enumObj: T, value: unknown): number {
    if (typeof value === 'number') return value;
    if (typeof value === 'string') {
      const n = enumObj[value];
      return typeof n === 'number' ? n : 0;
    }
    return 0;
  }

  private _loadExistingRule(): void {
    const plan = this.store.selectedPlan();
    const rule = plan?.rules.find((r) => r.id === this.ruleId);
    if (!rule) return;

    const rateTableTypeNum = this._enumToNumber(RateTableType, rule.rateTable.type);

    this.form.patchValue({
      name: rule.name,
      sortOrder: rule.sortOrder,
      measurement: {
        type: this._enumToNumber(MeasurementType, rule.measurement.type),
        sourceField: rule.measurement.sourceField,
        aggregation: this._enumToNumber(MeasurementAggregation, rule.measurement.aggregation),
      },
      rateTable: {
        type: rateTableTypeNum,
        flatRate: rule.rateTable.flatRate ?? 0.05,
        splitAtQuota: rule.rateTable.splitAtQuota ?? false,
      },
      hasTrigger: !!rule.trigger,
      hasModifier: !!rule.modifier,
      hasCap: !!rule.cap,
      hasFloor: !!rule.floor,
    });

    if (rule.trigger) {
      this.form.patchValue({
        trigger: { logicalOperator: this._enumToNumber(LogicalOperator, rule.trigger.logicalOperator) },
      });
      rule.trigger.conditions.forEach((c) => {
        this.conditionsArray.push(
          this.fb.nonNullable.group({
            field: [c.field, Validators.required],
            operator: [this._enumToNumber(ConditionOperator, c.operator)],
            valueType: [this._enumToNumber(ConditionValueType, c.value.type)],
            valueRaw: [c.value.raw],
          })
        );
      });
    }

    if (rule.modifier) {
      this.form.patchValue({
        modifier: {
          name: rule.modifier.name,
          type: this._enumToNumber(ModifierType, rule.modifier.type),
          factor: rule.modifier.factor,
        },
      });
    }

    if (rule.cap) {
      this.form.patchValue({ cap: { amount: rule.cap.amount.amount, scope: this._enumToNumber(CapScope, rule.cap.scope) } });
    }

    if (rule.floor) {
      this.form.patchValue({ floor: { amount: rule.floor.amount.amount } });
    }

    if (rateTableTypeNum === RateTableType.Tiered && rule.rateTable.tiers) {
      rule.rateTable.tiers.forEach((t) => {
        this.tiersArray.push(
          this.fb.nonNullable.group({ from: [t.from], to: [t.to], rate: [t.rate] })
        );
      });
    } else if (rateTableTypeNum === RateTableType.AttainmentBased && rule.rateTable.attainmentTiers) {
      rule.rateTable.attainmentTiers.forEach((t) => {
        this.attainmentTiersArray.push(
          this.fb.nonNullable.group({ attainmentFrom: [t.attainmentFrom], attainmentTo: [t.attainmentTo], rate: [t.rate] })
        );
      });
    }
  }

  addTier(): void { this._addTier(); }
  removeTier(i: number): void { this.tiersArray.removeAt(i); }

  addAttainmentTier(): void { this._addAttainmentTier(); }
  removeAttainmentTier(i: number): void { this.attainmentTiersArray.removeAt(i); }

  addCondition(): void {
    this.conditionsArray.push(
      this.fb.nonNullable.group({
        field: ['', Validators.required],
        operator: [ConditionOperator.Equal],
        valueType: [ConditionValueType.String],
        valueRaw: [''],
      })
    );
  }

  removeCondition(i: number): void { this.conditionsArray.removeAt(i); }

  private _addTier(): void {
    const last = this.tiersArray.controls.at(-1)?.value;
    const from = last ? (last.to ?? 0) : 0;
    this.tiersArray.push(
      this.fb.nonNullable.group({ from: [from], to: [null as number | null], rate: [0.05] })
    );
  }

  private _addAttainmentTier(): void {
    const last = this.attainmentTiersArray.controls.at(-1)?.value;
    const from = last ? (last.attainmentTo ?? 0) : 0;
    this.attainmentTiersArray.push(
      this.fb.nonNullable.group({ attainmentFrom: [from], attainmentTo: [null as number | null], rate: [0.05] })
    );
  }

  asFormGroup(ctrl: AbstractControl): FormGroup {
    return ctrl as FormGroup;
  }

  // Form values can be numeric (on create) or string names (when patched from API).
  // These helpers normalise to the enum name string so the i18n key is always correct.
  measurementTypeKey(value: unknown): string {
    if (value == null || value === '') return '';
    const asNum = Number(value);
    const name = !isNaN(asNum) ? (MeasurementType[asNum] ?? String(value)) : String(value);
    return `PLANS.MEASUREMENT_${name.toUpperCase()}`;
  }

  measurementAggregationKey(value: unknown): string {
    if (value == null || value === '') return '';
    const asNum = Number(value);
    const name = !isNaN(asNum) ? (MeasurementAggregation[asNum] ?? String(value)) : String(value);
    return `PLANS.AGGREGATION_${name.toUpperCase()}`;
  }

  async onSubmit(): Promise<void> {
    if (this.readOnly()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const currency = this.store.selectedPlan()?.currency ?? 'USD';

      const request: AddRuleRequest = {
        planId: this.planId,
        name: v.name,
        sortOrder: v.sortOrder,
        measurement: {
          _schema: 1,
          type: Number(v.measurement.type),
          sourceField: v.measurement.sourceField,
          aggregation: Number(v.measurement.aggregation),
        },
        rateTable: this._buildRateTable(v),
        trigger: v.hasTrigger ? this._buildTrigger(v) : null,
        modifier: v.hasModifier ? this._buildModifier(v) : null,
        cap: v.hasCap ? { _schema: 1, amount: { amount: v.cap.amount, currency }, scope: Number(v.cap.scope) } : null,
        floor: v.hasFloor ? { _schema: 1, amount: { amount: v.floor.amount, currency } } : null,
      };

      if (this.isEdit) {
        await this.store.updateRule(this.planId, this.ruleId!, { ...request, ruleId: this.ruleId! } as UpdateRuleRequest);
        this.toast.show('PLANS.TOAST_RULE_UPDATED', 'success');
      } else {
        await this.store.addRule(this.planId, request);
        this.toast.show('PLANS.TOAST_RULE_ADDED', 'success');
      }
      this.router.navigate(['/plans', this.planId]);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  private _buildRateTable(v: ReturnType<typeof this.form.getRawValue>) {
    const type = Number(v.rateTable.type) as RateTableType;
    return {
      _schema: 1 as const,
      type,
      flatRate: type === RateTableType.Flat ? v.rateTable.flatRate : null,
      tiers: type === RateTableType.Tiered
        ? v.rateTable.tiers.map((t) => ({ from: t['from'], to: t['to'], rate: t['rate'] }))
        : null,
      attainmentTiers: type === RateTableType.AttainmentBased
        ? v.rateTable.attainmentTiers.map((t) => ({ attainmentFrom: t['attainmentFrom'], attainmentTo: t['attainmentTo'], rate: t['rate'] }))
        : null,
      splitAtQuota: type === RateTableType.AttainmentBased ? (v.rateTable.splitAtQuota ?? false) : false,
    };
  }

  private _buildTrigger(v: ReturnType<typeof this.form.getRawValue>) {
    return {
      _schema: 1 as const,
      logicalOperator: v.trigger.logicalOperator,
      conditions: v.trigger.conditions.map((c) => ({
        field: c['field'],
        operator: Number(c['operator']),
        value: { type: Number(c['valueType']), raw: c['valueRaw'], set: null },
      })),
    };
  }

  private _buildModifier(v: ReturnType<typeof this.form.getRawValue>) {
    return {
      _schema: 1 as const,
      id: this.isEdit ? (this.store.selectedPlan()?.rules.find((r) => r.id === this.ruleId)?.modifier?.id ?? crypto.randomUUID()) : crypto.randomUUID(),
      name: v.modifier.name,
      type: Number(v.modifier.type),
      factor: v.modifier.factor,
      trigger: null,
    };
  }
}
