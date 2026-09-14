import { Component, computed, effect, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, throwError } from 'rxjs';
import {
  SelectOption,
  WsBadgeComponent,
  WsButtonComponent,
  WsCardComponent,
  WsConfirmationModalComponent,
  WsDatePickerComponent,
  WsGuideStep,
  WsGuideStepperComponent,
  WsInputComponent,
  WsModalComponent,
  WsPageLayoutComponent,
  WsSelectComponent,
  WsTabsComponent,
  WsTooltipDirective,
  type WsTab,
} from '../../shared/ui';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../shared/pipes/has-permission.pipe';
import { CurrencyFormatPipe } from '../../shared/pipes/currency-format.pipe';
import { formatRate } from '../../shared/utils/rate-format';
import { createRuleDefinitionForm } from '../plans/rule-form/rule-definition-form';
import { RuleDefinitionFieldsComponent } from '../plans/rule-form/rule-definition-fields.component';
import { RulePreviewComponent } from '../plans/rule-form/rule-preview.component';
import { FieldRequirement, SettingsApiService } from '../admin/services/settings.api.service';
import { EMPLOYMENT_TYPE_OPTIONS } from '../payees/form/payee-form.component';
import { reasonKey } from '../reconciliation/models/reconciliation-reason';
import { CalculatePayRunResult } from '../pay-runs/models/pay-run.model';
import { noPayoutsHeadlineKey, payRunSkipLabelKey } from '../pay-runs/models/pay-run-diagnostics';
import {
  configFingerprint, GuidedStepId, parseExperiment, SandboxExperiment, SandboxExperimentConfig,
  SandboxPromotionRecord, SandboxRule, SandboxUncreditedSale,
} from './models/guided-tour.model';
import { Rule } from '../plans/models/rule.model';
import { PlansApiService } from '../plans/services/plans.api.service';
import { ToastService } from '../../shared/services/toast.service';
import { SandboxIntroComponent } from './intro/sandbox-intro.component';
import { SandboxApiService } from './services/sandbox.api.service';
import { GUIDED_STEPS, GuidedTourStore } from './state/guided-tour.store';

/**
 * Una fecha en el formato que esperan los comandos.
 *
 * ★ SE ARMA CON LAS PARTES LOCALES, NO CON `toISOString()`. Esa función convierte a UTC primero: una
 * fecha creada a medianoche local en un huso por delante de Greenwich retrocede un día, y el período
 * del plan aparecía empezando el 31 de diciembre del año anterior. Se ve en pantalla, no compilando.
 */
function isoDate(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Las mediciones de la cuota que ofrece la pantalla real de cuotas, con los valores de SU enum. */
const QUOTA_REVENUE = 0;
const QUOTA_UNITS = 2;

@Component({
  selector: 'app-guided-tour',
  standalone: true,
  imports: [
    AppShellComponent, DatePipe, NgTemplateOutlet, IconComponent, WsTabsComponent, WsTooltipDirective, ReactiveFormsModule, TranslatePipe, WsPageLayoutComponent,
    WsCardComponent, WsBadgeComponent,
    WsButtonComponent, WsInputComponent, WsSelectComponent, WsDatePickerComponent, WsModalComponent,
    WsGuideStepperComponent, WsConfirmationModalComponent, RouterLink, HasPermissionPipe, CurrencyFormatPipe,
    RuleDefinitionFieldsComponent, RulePreviewComponent, SandboxIntroComponent,
  ],
  templateUrl: './guided-tour.component.html',
  styleUrl: './guided-tour.component.scss',
})
export class GuidedTourComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(SandboxApiService);
  private readonly translate = inject(TranslateService);
  readonly store = inject(GuidedTourStore);
  private readonly settingsApi = inject(SettingsApiService);

  readonly resetting = signal(false);

  private readonly today = new Date();
  private readonly yearStart = isoDate(new Date(this.today.getFullYear(), 0, 1));
  private readonly yearEnd = isoDate(new Date(this.today.getFullYear(), 11, 31));

  /**
   * Los datos del recorrido, con lo esencial de cada paso.
   *
   * ★★ VIENE RELLENO, Y ESO ES PARTE DEL DISEÑO. Lo que se practica aquí es entender el ciclo y probar
   * configuraciones, no teclear nombres. Todo es editable: quien venga a experimentar con sus propios
   * números — que es para lo que existe este sandbox — cambia lo que quiera.
   *
   * ★ LA REGLA NO ESTÁ AQUÍ. Vive en {@link ruleDef}, que es el mismo formulario de la pantalla real de
   * reglas; ver la nota de ese campo.
   */
  readonly form = this.fb.nonNullable.group({
    // Plan
    planName: ['Demo plan', Validators.required],
    planDescription: [''],
    currency: ['EUR', Validators.required],
    periodStart: [this.yearStart, Validators.required],
    periodEnd: [this.yearEnd, Validators.required],

    // Payee — lo que la empresa exija se marca como obligatorio desde `payeeRules` (Settings).
    payeeName: ['Alex Demo', Validators.required],
    employeeCode: ['DEMO-001', Validators.required],
    // Viene relleno: el email es obligatorio por defecto, y un paso de práctica que falla por un campo
    // vacío que nadie mencionó es exactamente el «Validation failed» que se está arreglando.
    payeeEmail: ['alex.demo@example.com', [Validators.email, Validators.maxLength(255)]],
    payeeRole: ['Sales representative', Validators.maxLength(100)],
    payeeEmploymentType: [''],
    payeeLocation: ['', Validators.maxLength(200)],

    // Cuota
    quotaAmount: [100000, [Validators.required, Validators.min(1)]],
    quotaMeasurement: [QUOTA_REVENUE, Validators.required],

    // Venta
    txReference: ['DEMO-0001', Validators.required],
    txAmount: [10000, [Validators.required, Validators.min(1)]],
    txDate: [isoDate(this.today), Validators.required],
    txDescription: [''],
  });

  /**
   * La regla, con TODAS las opciones de la pantalla real — porque ES la pantalla real.
   *
   * ★★ EL MISMO FORMULARIO, NO UNA COPIA. La primera versión del recorrido tenía una regla escrita a
   * mano con cinco campos, y en una semana ya contaba otra historia que la pantalla de Planes: el tope
   * y el mínimo viajaban como `{ type, value }` y los tramos por cumplimiento como `{ from, to }`,
   * formas que el servidor no lee — llegaban a cero y nada fallaba. Ahora el recorrido y Planes
   * comparten `RuleDefinitionForm`: mismos campos (tabla plana, por tramos o por cumplimiento,
   * disparador, modificador, tope y mínimo), mismos avisos, mismo constructor del cuerpo y el mismo
   * simulador. Cuando la regla gane una opción en Planes, aparece aquí sin tocar esta pantalla.
   *
   * Lo único propio del recorrido es a qué plan pertenece la regla y adónde se manda la simulación:
   * al plan de práctica, por la ruta del sandbox.
   */
  readonly ruleDef = createRuleDefinitionForm({
    planId: () => this.store.status().plan?.id ?? '',
    currency: computed(() => this.store.status().plan?.currency ?? this.form.controls.currency.value),
    readOnly: signal(false),
    simulate: (request) => {
      const planId = this.store.status().plan?.id;
      // Sin plan no hay moneda ni contexto que simular; se dice lo que falta en vez de inventarlo.
      return planId
        ? this.api.simulateRule(planId, request)
        : throwError(() => new HttpErrorResponse({ status: 422, error: { message: 'GUIDED.BLOCKED.NEEDS_PLAN' } }));
    },
  });

  readonly currencyOptions: SelectOption[] = [
    { value: 'EUR', label: 'EUR' },
    { value: 'USD', label: 'USD' },
    { value: 'PLN', label: 'PLN' },
  ];

  /**
   * ★ LAS DOS QUE OFRECE LA PANTALLA REAL DE CUOTAS, NO TRES. La lista anterior añadía «Margen», que el
   * formulario real de cuotas no ofrece: el recorrido enseñaba una opción del producto que no existe.
   * Los valores son los del enum de CUOTAS (Units = 2), que no es el de la medición de una regla.
   */
  readonly measurementOptions: SelectOption[] = [
    { value: QUOTA_REVENUE, label: 'GUIDED.MEASUREMENT.REVENUE' },
    { value: QUOTA_UNITS, label: 'GUIDED.MEASUREMENT.UNITS' },
  ];

  // ── Las reglas de la empresa para un payee ────────────────────────────────────────
  //
  // ★★ EL SANDBOX APLICA LAS FIELD REQUIREMENTS REALES DE LA EMPRESA. `FieldRequirementSetting` no está
  // en la lista de tablas del sandbox (ApplicationDbContext.SandboxedEntities): el validador de
  // `CreatePayeeCommand` lee la configuración de verdad. Si aquí no se sabe, el paso manda vacío un
  // campo que la empresa exige y la única respuesta es «Validation failed». Se leen del MISMO endpoint y
  // con los MISMOS valores por defecto que el formulario real de Payees.

  readonly employmentTypeOptions = EMPLOYMENT_TYPE_OPTIONS;
  private readonly fieldRequirements = signal<FieldRequirement[]>([]);

  private requirement(field: string, fallback: boolean): boolean {
    return this.fieldRequirements().find(r => r.fieldName === field)?.isRequired ?? fallback;
  }

  readonly payeeRules = computed(() => ({
    email: this.requirement('Email', true),
    role: this.requirement('Role', false),
    manager: this.requirement('ManagerId', false),
    employmentType: this.requirement('EmploymentType', false),
    location: this.requirement('Location', false),
  }));

  /** Si la empresa exige algo más que lo básico: entonces se avisa de dónde sale la regla. */
  readonly payeeHasCompanyRules = computed(() => {
    const r = this.payeeRules();
    return r.role || r.employmentType || r.location || r.manager;
  });

  /** Los validadores del paso siguen a la configuración, como en el formulario real. */
  private readonly syncPayeeRules = effect(() => {
    const r = this.payeeRules();
    const c = this.form.controls;
    const sync = (ctrl: (typeof c)[keyof typeof c], required: boolean) => {
      if (required) ctrl.addValidators(Validators.required);
      else ctrl.removeValidators(Validators.required);
      ctrl.updateValueAndValidity({ emitEvent: false });
    };
    sync(c.payeeEmail, r.email);
    sync(c.payeeRole, r.role);
    sync(c.payeeEmploymentType, r.employmentType);
    sync(c.payeeLocation, r.location);
  });

  readonly currentStep = computed(() => this.store.currentStep());

  /**
   * La pestaña del lateral: el recorrido o el historial de experimentos.
   *
   * ★ EL HISTORIAL VIVE AQUÍ, NO AL FINAL DE LA PÁGINA. Abajo del todo nadie lo encontraba, y es la
   * mitad del sandbox que sirve para comparar configuraciones. `ws-tabs`: un grupo de botones unidos a
   * todo el ancho de la tarjeta, que se lee como parte de ella (no dos pastillas sueltas).
   */
  readonly asideTab = signal('steps');
  readonly asideTabs: WsTab[] = [
    { value: 'steps', label: 'GUIDED.TABS.STEPS', icon: 'list' },
    { value: 'experiments', label: 'GUIDED.TABS.EXPERIMENTS', icon: 'archive' },
  ];

  /**
   * Los pasos, ya traducidos: la primitiva recibe texto, no claves.
   *
   * ★★ EL MOTIVO DEL BLOQUEO NO VIAJA AL STEPPER, Y ES DELIBERADO. El mismo hecho se estaba contando
   * dos veces en la misma pantalla — una frase bajo el formulario y otra distinta en la lista — y dos
   * mensajes para un solo bloqueo hacen dudar de los dos. El stepper muestra el ESTADO (candado,
   * atenuado); el motivo se explica una sola vez, junto al botón que no se puede pulsar. La primitiva
   * sigue admitiéndolo para quien lo necesite en otra pantalla.
   */
  readonly steps = computed<WsGuideStep[]>(() => {
    const editing = this.editMode();
    const current = this.currentStep();
    return this.store.steps().map(step => ({
      ...step,
      // ★ EDITANDO NO HAY CANDADOS: se salta al paso que se quiere cambiar. El orden real del sistema
      // se respeta igual — al actualizar, los pasos se vuelven a ejecutar uno tras otro.
      state: editing ? (step.id === current ? 'current' : 'available') : step.state,
      title: this.translate.instant(step.title),
      description: step.description ? this.translate.instant(step.description) : undefined,
      blockedReason: undefined,
    }));
  });

  readonly blockedReason = computed(() => {
    // Editando, nada se ejecuta paso a paso: no hay bloqueo que explicar.
    if (this.editMode()) return null;
    const step = this.currentStep();
    const reason = this.store.blockedReasonKey(step);
    if (reason) return reason;

    // ★ UN MANAGER OBLIGATORIO NO SE PUEDE CUMPLIR AQUÍ. El primer payee del sandbox no tiene a quién
    // elegir, y un payee real como manager apuntaría a una fila que no existe en el esquema de práctica.
    // Se dice por qué y dónde se cambia, en vez de dejar que el servidor responda «Validation failed».
    return step === 'payee' && this.payeeRules().manager ? 'GUIDED.BLOCKED.NEEDS_MANAGER_SETTING' : null;
  });
  readonly isDone = computed(() => this.store.isDone(this.currentStep()));

  /**
   * Por qué un paso ya hecho no se puede volver a ejecutar, o `null` si se puede.
   *
   * ★ SÓLO LAS TRANSICIONES IRREVERSIBLES. Aprobar un pay run ya aprobado o pagar uno ya pagado no es
   * experimentar: es pedirle al servidor algo que va a rechazar, con el botón encendido invitando a
   * hacerlo. Los pasos que crean algo (un plan, una venta…) sí se pueden repetir: crear otro dato de
   * práctica es justo para lo que está el sandbox.
   */
  readonly lockedReason = computed(() => {
    if (this.editMode()) return null;
    const step = this.currentStep();
    if (!this.store.isDone(step)) return null;
    switch (step) {
      case 'approve': return 'GUIDED.LOCKED.APPROVED';
      case 'pay': return 'GUIDED.LOCKED.PAID';
      default: return null;
    }
  });
  readonly finished = computed(() => this.store.completed() === GUIDED_STEPS.length);

  /** La foto del experimento que se está revisando, o `null` si se está en el recorrido en curso. */
  readonly reviewed = computed(() => {
    this.store.openExperiment();
    return this.store.reviewedStatus();
  });

  ngOnInit(): void {
    void this.store.refresh();
    void this.store.loadExperiments();

    // Una lectura fallida deja la lista vacía: se aplican los valores por defecto de Payees y, si el
    // servidor exige algo más, el error del paso ya dice qué.
    this.settingsApi.getFieldRequirements().subscribe({
      next: requirements => this.fieldRequirements.set(requirements),
      error: () => this.fieldRequirements.set([]),
    });

    this.ruleDef.loadCatalogs();
    this.resetRuleForm();
  }

  /** Los campos que este paso necesita. La zona de acción no muestra ni uno de más. */
  shows(field: string): boolean {
    const fields: Record<GuidedStepId, string[]> = {
      plan: ['planName', 'planDescription', 'currency', 'periodStart', 'periodEnd'],
      // La regla no usa esta lista: pinta el formulario de reglas completo (ver la plantilla).
      rule: [],
      // Tipo de empleo y ubicación sólo si la empresa los exige: el paso sigue siendo lo esencial.
      payee: [
        'payeeName', 'employeeCode', 'payeeEmail', 'payeeRole',
        ...(this.payeeRules().employmentType ? ['payeeEmploymentType'] : []),
        ...(this.payeeRules().location ? ['payeeLocation'] : []),
      ],
      quota: ['quotaAmount', 'quotaMeasurement'],
      assignment: ['periodStart', 'periodEnd'],
      transaction: ['txReference', 'txAmount', 'txDate', 'txDescription'],
      calculate: [],
      payrun: ['periodStart', 'periodEnd'],
      approve: [],
      pay: [],
    };
    return fields[this.currentStep()].includes(field);
  }

  /** Si al paso abierto le falta algo por rellenar. La regla se valida con su propio formulario. */
  stepInvalid(): boolean {
    return this.currentStep() === 'rule' ? this.ruleDef.form.invalid : this.form.invalid;
  }

  select(id: string): void {
    this.store.open(id);
  }

  /** Enviar el formulario: editando, actualiza el experimento; si no, ejecuta el paso abierto. */
  submit(): void {
    void (this.editMode() ? this.updateExperiment() : this.execute());
  }

  /**
   * Ejecuta el paso abierto llamando al comando real del producto.
   *
   * ★ CADA RAMA ES UNA LLAMADA Y NADA MÁS. Aquí no se decide qué es una comisión ni cuándo se puede
   * pagar un pay run: eso lo decide el motor, igual que en las pantallas reales. Si mañana el motor
   * cambia una regla, este recorrido enseña la nueva sin tocar una línea.
   */
  async execute(): Promise<void> {
    const step = this.currentStep();

    // Un paso irreversible que ya se hizo no se repite, aunque algo pulsara el botón apagado.
    if (this.lockedReason()) return;

    // ★ UN CAMPO QUE FALTA SE SEÑALA EN EL CAMPO, como en el formulario real de Payees. El botón ya no se
    // apaga por esto: apagado no decía cuál faltaba. Pulsar marca los campos y cada uno dice su error.
    if (this.stepInvalid()) {
      (step === 'rule' ? this.ruleDef.form : this.form).markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const s = this.store.status();
    const rules = this.payeeRules();

    await this.store.run(() => {
      switch (step) {
        case 'plan':
          return firstValueFrom(this.api.createPlan({
            name: v.planName,
            description: v.planDescription || this.translate.instant('GUIDED.STEP.PLAN.DESCRIPTION_VALUE'),
            effectiveStart: v.periodStart,
            effectiveEnd: v.periodEnd,
            currency: v.currency,
          }));

        case 'rule':
          // Numerada detrás de las que el plan de práctica ya tenga, igual que en Planes.
          this.ruleDef.form.controls.sortOrder.setValue((s.plan?.ruleCount ?? 0) + 1);
          // ★ EL CUERPO LO ARMA EL MISMO CONSTRUCTOR QUE GUARDA EN PLANES. Nada se traduce aquí.
          return firstValueFrom(this.api.addRule(this.ruleDef.buildDefinition()));

        case 'payee':
          return firstValueFrom(this.api.createPayee({
            fullName: v.payeeName,
            employeeCode: v.employeeCode,
            email: v.payeeEmail || null,
            hireDate: v.periodStart,
            role: v.payeeRole || null,
            // Sólo si la empresa los exige: son los únicos casos en que el paso los enseña.
            employmentType: rules.employmentType ? v.payeeEmploymentType || null : null,
            location: rules.location ? v.payeeLocation || null : null,
          }));

        case 'quota':
          return firstValueFrom(this.api.createQuota({
            payeeId: s.payee!.id,
            planId: s.plan!.id,
            measurementType: Number(v.quotaMeasurement),
            amount: v.quotaAmount,
            currency: v.currency,
            periodStart: v.periodStart,
            periodEnd: v.periodEnd,
          }));

        case 'assignment':
          return firstValueFrom(this.api.assign({
            planId: s.plan!.id,
            payeeId: s.payee!.id,
            effectiveStart: v.periodStart,
            effectiveEnd: v.periodEnd,
          }));

        case 'transaction':
          return firstValueFrom(this.api.ingestTransaction({
            referenceNumber: v.txReference,
            payeeId: s.payee!.id,
            amount: v.txAmount,
            currency: v.currency,
            transactionDate: v.txDate,
            description: v.txDescription || null,
            // El recorrido calcula en su propio paso, para que el usuario vea las dos mitades.
            processImmediately: false,
          }));

        case 'calculate':
          // Se guarda lo que respondió el motor: si alguna venta no generó comisión, el paso lo dice.
          return firstValueFrom(this.api.process()).then(result => {
            // ★ SE NORMALIZA: una respuesta sin lista (un servidor más viejo, un cuerpo inesperado) no
            // puede apagar el aviso. Haber calculado ya basta para que la pantalla diga algo.
            this.store.lastCalculation.set({
              creditsCreated: result?.creditsCreated ?? 0,
              uncredited: Array.isArray(result?.uncredited) ? result.uncredited : [],
            });
            return result;
          });

        case 'payrun':
          // Se guarda el diagnóstico del motor: si no creó payouts o descartó asignaciones, se dice por qué.
          return firstValueFrom(this.api.calculatePayRun({
            periodStart: v.periodStart,
            periodEnd: v.periodEnd,
          })).then(result => {
            this.store.lastPayRun.set(result ?? null);
            return result;
          });

        case 'approve':
          return firstValueFrom(this.api.approvePayRun({ payRunId: s.payRun!.id }));

        case 'pay':
          return firstValueFrom(this.api.markPaid({ payRunId: s.payRun!.id }));
      }
    }, step);

    // Hecho el paso, la tarjeta ya enseña el siguiente: se lleva al usuario a su principio en vez de
    // dejarlo a media página, donde estaba el botón. Si falló, no se mueve: el error está junto al botón.
    if (!this.store.error()) this.scrollToActiveStep();
  }

  /** La tarjeta donde se actúa: a su principio se vuelve después de cada paso. */
  private readonly actionCard = viewChild('actionCard', { read: ElementRef });

  /**
   * Lleva la tarjeta del paso al principio de la vista.
   *
   * ★ `scrollIntoView`, NO `window.scrollTo`. La página no desplaza la ventana: desplaza
   * `.shell__content`, y es el navegador quien sabe qué contenedor mover. El banner fijo de arriba se
   * respeta con `scroll-margin-top` en la tarjeta. Sin animación si el sistema pide reducir movimiento.
   */
  private scrollToActiveStep(): void {
    const card = this.actionCard()?.nativeElement as HTMLElement | undefined;
    if (!card) return;
    const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    card.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' });
  }

  // ── Ventas que no generaron comisión ──────────────────────────────────────────────

  /**
   * La clave que explica por qué una venta no generó comisión.
   *
   * ★ LISTA BLANCA, NUNCA `GUIDED.UNCREDITED.${reason}` (§C2). Un código que esta versión no conozca
   * cae a la frase genérica, nunca a un identificador interno en pantalla.
   */
  uncreditedKey(reason: string): string {
    switch (reason) {
      case 'NoPayee': return 'GUIDED.UNCREDITED.NO_PAYEE';
      case 'NoActiveAssignment': return 'GUIDED.UNCREDITED.NO_ASSIGNMENT';
      case 'CurrencyMismatch': return 'GUIDED.UNCREDITED.CURRENCY';
      case 'NoApplicableRules': return 'GUIDED.UNCREDITED.NO_RULES';
      case 'TriggerNotMatched': return 'GUIDED.UNCREDITED.TRIGGER';
      default: return 'GUIDED.UNCREDITED.UNKNOWN';
    }
  }

  uncreditedParams(sale: SandboxUncreditedSale): Record<string, string> {
    return { reference: sale.referenceNumber, rules: sale.ruleNames.join(', ') };
  }

  // ── Lo que el motor explica igual que en el producto real ─────────────────────────

  /**
   * El motivo de un crédito que la tabla de tasas se negó a tasar, con la MISMA lista blanca que el
   * Centro de Reconciliación. Un crédito rechazado no puede verse como una comisión normal.
   */
  refusalKey(code: string | null | undefined): string {
    return reasonKey(code);
  }

  /** El titular de un pay run que no creó payouts, con el mismo orden de causas que la lista real. */
  payRunHeadlineKey(run: CalculatePayRunResult): string {
    return noPayoutsHeadlineKey(run);
  }

  payRunHeadlineParams(run: CalculatePayRunResult): Record<string, number> {
    return {
      considered: run.diagnostics?.assignmentsConsidered ?? 0,
      count: run.diagnostics?.skipped?.find(s => s.code === 'UnreachableCommission')?.count ?? 0,
    };
  }

  payRunSkipKey(code: string): string {
    return payRunSkipLabelKey(code);
  }

  // ── El resumen de la regla creada ─────────────────────────────────────────────────

  /**
   * La clave que nombra el tipo de tabla.
   *
   * ★ LISTA BLANCA, NUNCA `PLANS.RATE_TABLE_${tipo}`. El servidor manda el nombre del enum; antes se
   * pintaba tal cual («AttainmentBased») y, concatenado, un tipo que esta versión no conozca imprimiría
   * un identificador interno. Lo desconocido cae a una etiqueta genérica.
   */
  tableTypeKey(type: string): string {
    switch (type) {
      case 'Flat': return 'PLANS.RATE_TABLE_FLAT';
      case 'Tiered': return 'PLANS.RATE_TABLE_TIERED';
      case 'AttainmentBased': return 'PLANS.RATE_TABLE_ATTAINMENTBASED';
      default: return 'GUIDED.RESULT.RULE_TYPE_UNKNOWN';
    }
  }

  /**
   * La tasa plana escrita según lo que significa: 0.05 es «5 %» sobre importes, y 5 es «5,00 € por
   * unidad» midiendo unidades. Pasa por `rate-format.ts`, que es el único sitio del producto que decide
   * eso; `null` cuando la tabla no es plana y no hay una tasa única que enseñar.
   */
  ruleRate(rule: SandboxRule, currency: string): string | null {
    if (rule.rate === null || rule.rate === undefined) return null;
    const base = rule.measurementType === 'Units' ? 'TransactionQuantity' : 'TransactionAmount';
    return formatRate(
      rule.rate, base, currency,
      this.translate.currentLang || 'en',
      this.translate.instant('GUIDED.RESULT.PER_UNIT'),
    );
  }

  // ── Historial de experimentos ─────────────────────────────────────────────────────

  /**
   * El nombre del experimento se pide en un modal del sistema.
   *
   * ★ NO CON `window.prompt`. El diálogo del navegador es justo lo que el design system prohíbe
   * (§5.4): no se puede traducir su botón, no respeta el tema y bloquea la página entera.
   *
   * ★ EL NOMBRE SE PIDE, NO SE INVENTA. «Experimento 3» no le dice nada a nadie dentro de un mes; el
   * usuario sabe qué estaba probando y es quien puede nombrarlo.
   */
  readonly nameModalOpen = signal(false);
  readonly nameControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly savingName = signal(false);

  /** Qué hace el modal: guardar la ejecución actual, o sólo renombrar un experimento. */
  readonly nameMode = signal<'save' | 'rename'>('save');

  /** El experimento que se está renombrando, o `null`. */
  readonly renamingId = signal<string | null>(null);

  /**
   * El experimento cargado en el sandbox para editarlo, o `null`. Mientras hay uno, guardar lo
   * ACTUALIZA (con la opción de guardar como nuevo): es lo que significa «editar y volver a correr».
   */
  readonly loadedExperiment = signal<SandboxExperiment | null>(null);

  saveExperiment(): void {
    this.nameMode.set('save');
    this.renamingId.set(null);
    this.nameControl.setValue(
      this.loadedExperiment()?.name
        ?? this.store.status().plan?.name
        ?? this.translate.instant('GUIDED.EXPERIMENTS.DEFAULT_NAME'),
    );
    this.nameModalOpen.set(true);
  }

  renameExperiment(experiment: SandboxExperiment): void {
    this.nameMode.set('rename');
    this.renamingId.set(experiment.id);
    this.nameControl.setValue(experiment.name);
    this.nameModalOpen.set(true);
  }

  /** @param asNew guarda la ejecución como un experimento nuevo aunque haya uno cargado. */
  async confirmName(asNew = false): Promise<void> {
    const name = this.nameControl.value.trim();
    if (!name) {
      this.nameControl.markAsTouched();
      return;
    }

    this.savingName.set(true);
    try {
      if (this.nameMode() === 'rename') {
        // Renombrar cambia el nombre y nada más: la foto del experimento viaja intacta.
        const target = this.store.experiments().find(e => e.id === this.renamingId());
        if (target) await this.store.renameExperiment(target, name);
        this.toasts.show('GUIDED.EXPERIMENTS.TOAST_RENAMED', 'success', { name });
      } else {
        const config = await this.buildConfig();
        const loaded = asNew ? null : this.loadedExperiment();
        // Actualizar un experimento no borra que se promovió: si la configuración cambió, la huella ya
        // no coincide y se podrá volver a promover; si no cambió, sigue protegido contra el duplicado.
        const promotion = loaded ? parseExperiment(loaded.snapshot).promotion : null;
        // Lo guardado pasa a ser «el cargado»: el siguiente guardar lo actualiza en vez de duplicarlo.
        this.loadedExperiment.set(await this.store.saveExperiment(name, loaded?.id ?? null, config, promotion));
        // ★ GUARDAR TIENE QUE DECIRLO. Sin confirmación el usuario no sabe si se guardó, y vuelve a pulsar.
        this.toasts.show(loaded ? 'GUIDED.EXPERIMENTS.TOAST_UPDATED' : 'GUIDED.EXPERIMENTS.TOAST_SAVED', 'success', { name });
      }
      this.nameModalOpen.set(false);
      // Se enseña dónde quedó: el historial está en la otra pestaña del lateral.
      this.asideTab.set('experiments');
    } finally {
      this.savingName.set(false);
    }
  }

  /**
   * Lo que se introdujo, listo para volver a llenar los formularios.
   *
   * ★ LAS REGLAS SE LEEN DEL SERVIDOR, COMPLETAS, con la misma consulta que usa Planes: son las que de
   * verdad se crearon en el plan de práctica, no lo que quede escrito en el formulario. Si la lectura
   * falla, el experimento se guarda igual —sin reglas— en vez de perder la prueba.
   */
  private async buildConfig(): Promise<SandboxExperimentConfig> {
    const v = this.form.getRawValue();
    const plan = this.store.status().plan;
    const rules = plan
      ? ((await firstValueFrom(this.api.getPlan(plan.id)).catch(() => null))?.rules ?? []).filter(r => r.isActive)
      : [];

    return {
      plan: {
        name: plan?.name ?? v.planName,
        description: v.planDescription,
        currency: plan?.currency ?? v.currency,
        periodStart: plan?.effectiveStart ?? v.periodStart,
        periodEnd: plan?.effectiveEnd ?? v.periodEnd,
      },
      rules,
      payee: {
        fullName: v.payeeName, employeeCode: v.employeeCode, email: v.payeeEmail, role: v.payeeRole,
        employmentType: v.payeeEmploymentType, location: v.payeeLocation,
      },
      quota: { amount: v.quotaAmount, measurement: Number(v.quotaMeasurement) },
      transaction: { reference: v.txReference, amount: v.txAmount, date: v.txDate, description: v.txDescription },
    };
  }

  /**
   * El resultado de cada experimento, para enseñarlo en su fila: lo que se quiere comparar de un vistazo.
   *
   * ★ SE LEE, NO SE CALCULA. Es el total del payout si lo hubo, o la comisión del crédito si hubo uno
   * solo — tal como los guardó el servidor. Sumar créditos aquí sería aritmética de dinero en un
   * componente (§5.7); con varios créditos y sin payout, la fila no enseña importe.
   */
  readonly experimentResults = computed(() => {
    const results = new Map<string, { amount: number; currency: string } | null>();
    for (const experiment of this.store.experiments()) {
      results.set(experiment.id, this.resultOf(experiment));
    }
    return results;
  });

  private resultOf(experiment: SandboxExperiment): { amount: number; currency: string } | null {
    // `parseExperiment` lee los dos formatos y no revienta con una foto ilegible: la fila sale sin importe.
    const s = parseExperiment(experiment.snapshot).status;
    if (s.payouts?.length === 1) return { amount: s.payouts[0].total, currency: s.payouts[0].currency };
    if (s.credits?.length === 1) return { amount: s.credits[0].creditedAmount, currency: s.credits[0].currency };
    return null;
  }

  /**
   * El experimento a punto de borrarse, o `null`.
   *
   * ★★ BORRAR PREGUNTA ANTES. Antes un clic en «Delete» lo eliminaba sin más: una prueba de media
   * hora perdida por un clic de más, sin forma de deshacerlo. Ahora abre la confirmación del sistema,
   * que nombra el experimento y dice qué NO se toca.
   */
  readonly deleteTarget = signal<SandboxExperiment | null>(null);
  readonly deleting = signal(false);

  askDelete(experiment: SandboxExperiment): void {
    this.deleteTarget.set(experiment);
  }

  async confirmDelete(): Promise<void> {
    const target = this.deleteTarget();
    if (!target) return;

    this.deleting.set(true);
    try {
      await this.store.deleteExperiment(target.id);
      if (this.loadedExperiment()?.id === target.id) this.loadedExperiment.set(null);
      this.deleteTarget.set(null);
    } finally {
      this.deleting.set(false);
    }
  }

  // ── Cargar un experimento para editarlo y volver a correrlo ───────────────────────

  /** La configuración del experimento abierto, o `null` si es de antes de que se guardaran. */
  readonly reviewedConfig = computed(() => {
    const open = this.store.openExperiment();
    return open ? parseExperiment(open.snapshot).config : null;
  });

  /**
   * Editando un experimento: los formularios llevan su configuración y los pasos se abren libremente.
   *
   * ★★ ACTUALIZAR NO ES VOLVER A HACER EL RECORRIDO. Obligar a pulsar diez pasos para cambiar una tasa
   * es castigar al que ya sabe usar el sandbox. Se salta al paso que se quiere cambiar, se cambia, y
   * «Actualizar experimento» vuelve a ejecutar todos los pasos solo, con los comandos reales, en el
   * orden del sistema. Los resultados siguen saliendo del motor, no de lo que se escribió.
   */
  readonly editMode = signal(false);

  /** Las reglas del experimento en edición, y cuál está en el formulario. */
  readonly editRules = signal<Rule[]>([]);
  readonly editRuleIndex = signal(0);
  readonly editRuleTabs = computed<WsTab[]>(() =>
    this.editRules().map((rule, i) => ({ value: String(i), label: rule.name || `#${i + 1}` })));

  readonly updating = signal(false);

  readonly loadTarget = signal<SandboxExperiment | null>(null);
  readonly promoteExperimentTarget = signal<SandboxExperiment | null>(null);
  readonly loadingExperiment = signal(false);

  async confirmLoad(): Promise<void> {
    const target = this.loadTarget();
    if (!target) return;
    this.loadingExperiment.set(true);
    try {
      await this.applyExperiment(target);
    } finally {
      this.loadingExperiment.set(false);
      this.loadTarget.set(null);
    }
  }

  /**
   * Promueve un experimento guardado a un plan real.
   *
   * ★★ NO HAY UN CAMINO NUEVO HACIA EL DINERO REAL. Primero se carga el experimento y se vuelven a crear
   * su plan y sus reglas EN EL SANDBOX, con los comandos reales, exactamente como si el usuario pulsara
   * los pasos. Después se abre la misma confirmación de promoción de siempre, que cruza por el mismo
   * endpoint auditado. Nada real se crea antes de que el usuario diga que sí en ese modal.
   */
  async confirmPromoteExperiment(): Promise<void> {
    const target = this.promoteExperimentTarget();
    if (!target) return;
    this.loadingExperiment.set(true);
    try {
      // Antes de recrear nada: si esta misma configuración ya está en producción, se dice y se para.
      const config = parseExperiment(target.snapshot).config;
      const existing = config ? await this.livePromotionOf(config) : null;
      if (existing) {
        this.duplicatePromotion.set({ record: existing, where: 'experiment' });
        return;
      }

      if (!(await this.applyExperiment(target))) return;

      // Se recrea la prueba completa, hasta donde la dejó el experimento: así, al guardarse con la
      // promoción, conserva sus resultados en vez de quedarse sólo con el plan y la regla.
      const failed = await this.replay(this.targetsOf(target));
      this.editMode.set(false);
      if (failed) {
        this.store.open(failed);
        return;
      }
      if (this.store.canPromote()) this.promoteOpen.set(true);
    } finally {
      this.loadingExperiment.set(false);
      this.promoteExperimentTarget.set(null);
    }
  }

  /**
   * Vacía el sandbox y llena todos los formularios con la configuración del experimento.
   *
   * ★ EL SANDBOX SE VACÍA, LOS EXPERIMENTOS NO. `SandboxReset` borra los datos del ciclo y no toca la
   * tabla de experimentos; la confirmación avisa de que la práctica actual se pierde si no se guardó.
   */
  private async applyExperiment(experiment: SandboxExperiment): Promise<boolean> {
    const config = parseExperiment(experiment.snapshot).config;
    if (!config) return false;

    await this.store.reset();
    if (this.store.error()) return false;

    this.form.patchValue({
      planName: config.plan.name,
      planDescription: config.plan.description,
      currency: config.plan.currency,
      periodStart: config.plan.periodStart,
      periodEnd: config.plan.periodEnd,
      payeeName: config.payee.fullName,
      employeeCode: config.payee.employeeCode,
      payeeEmail: config.payee.email,
      payeeRole: config.payee.role,
      payeeEmploymentType: config.payee.employmentType,
      payeeLocation: config.payee.location,
      quotaAmount: config.quota.amount,
      quotaMeasurement: config.quota.measurement,
      txReference: config.transaction.reference,
      txAmount: config.transaction.amount,
      txDate: config.transaction.date,
      txDescription: config.transaction.description,
    });

    this.editRules.set(config.rules);
    this.editRuleIndex.set(0);
    if (config.rules[0]) this.ruleDef.loadRule(config.rules[0]);
    this.editMode.set(true);

    // ★ EL PLAN DE PRÁCTICA SE CREA AL CARGAR. El simulador de la regla calcula contra un plan real del
    // sandbox (moneda, periodo, cuota); sin él respondía «Crea primero el plan» mientras se editaba.
    // Se crea con el mismo comando del paso «Crear plan». Al actualizar, el sandbox se vacía y el plan
    // se vuelve a crear con lo editado.
    this.store.open('plan');
    await this.execute();
    this.store.open('plan');

    this.loadedExperiment.set(experiment);
    this.store.review(null);
    this.asideTab.set('steps');
    this.scrollToActiveStep();
    return true;
  }

  /** Cambia la regla que se edita, sin perder lo que se cambió en la anterior. */
  selectEditRule(index: string): void {
    const i = Number(index);
    if (i === this.editRuleIndex()) return;
    this.captureCurrentRule();
    this.editRuleIndex.set(i);
    const rule = this.editRules()[i];
    if (rule) this.ruleDef.loadRule(rule);
  }

  /** Guarda en la lista lo que hay en el formulario de regla (misma forma que la regla del servidor). */
  private captureCurrentRule(): void {
    const rules = [...this.editRules()];
    const current = rules[this.editRuleIndex()];
    if (!current) return;
    rules[this.editRuleIndex()] = { ...current, ...this.ruleDef.buildDefinition() } as Rule;
    this.editRules.set(rules);
  }

  /**
   * Vuelve a ejecutar el experimento editado y lo guarda.
   *
   * ★ HASTA DONDE LLEGABA EL EXPERIMENTO. Si la prueba original llegó a pagar, se vuelve a pagar; si
   * se quedó en calcular, se queda ahí. Si un paso falla, se abre ESE paso con su error y no se guarda
   * nada: el experimento sigue como estaba hasta que la nueva ejecución sale completa.
   */
  async updateExperiment(): Promise<void> {
    const experiment = this.loadedExperiment();
    if (!experiment || this.updating()) return;

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toasts.show('GUIDED.EDIT.INVALID', 'error');
      return;
    }
    if (this.editRules().length > 0 && this.ruleDef.form.invalid) {
      this.store.open('rule');
      this.ruleDef.form.markAllAsTouched();
      this.toasts.show('GUIDED.EDIT.INVALID', 'error');
      return;
    }

    this.captureCurrentRule();
    this.updating.set(true);
    try {
      await this.store.reset();
      const failed = this.store.error() ? 'plan' : await this.replay(this.targetsOf(experiment));
      if (failed) {
        this.store.open(failed);
        this.toasts.show('GUIDED.EDIT.STOPPED', 'error', {
          step: this.translate.instant(`GUIDED.STEP.${failed.toUpperCase()}.TITLE`),
        });
        return;
      }

      const before = parseExperiment(experiment.snapshot);
      const config = await this.buildConfig();
      // El experimento se llamaba como su plan: si el plan cambió de nombre, el experimento también.
      const name = before.config && experiment.name === before.config.plan.name ? config.plan.name : experiment.name;
      const saved = await this.store.saveExperiment(name, experiment.id, config, before.promotion);

      this.loadedExperiment.set(saved);
      this.editMode.set(false);
      this.store.followProgress();
      this.toasts.show('GUIDED.EXPERIMENTS.TOAST_UPDATED', 'success', { name });
      this.scrollToActiveStep();
    } finally {
      this.updating.set(false);
    }
  }

  /** Deja de editar: el sandbox y los formularios vuelven a empezar. */
  async cancelEdit(): Promise<void> {
    await this.clearScreen();
  }

  /** Los pasos que la ejecución guardada del experimento llegó a completar, en orden. */
  private targetsOf(experiment: SandboxExperiment): GuidedStepId[] {
    const { status } = parseExperiment(experiment.snapshot);
    const done = GUIDED_STEPS.filter(id => this.store.isDone(id, status));
    return done.length >= 2 ? done : ['plan', 'rule'];
  }

  /**
   * Ejecuta los pasos en orden con los comandos reales; todas las reglas en el paso de regla.
   * Devuelve el paso que no se pudo completar, o `null` si salieron todos.
   */
  private async replay(targets: readonly GuidedStepId[]): Promise<GuidedStepId | null> {
    for (const step of targets) {
      // El plan ya existe si se creó al cargar el experimento (promover desde el historial): no se duplica.
      if (step === 'plan' && this.store.isDone('plan')) continue;
      if (step === 'rule') {
        for (const rule of this.editRules()) {
          this.ruleDef.loadRule(rule);
          this.store.open('rule');
          await this.execute();
          if (this.store.error()) return 'rule';
        }
        continue;
      }
      this.store.open(step);
      await this.execute();
      if (this.store.error() || !this.store.isDone(step)) return step;
    }
    return null;
  }

  // ── Promoción a plan real ─────────────────────────────────────────────────────────

  readonly promoteOpen = signal(false);

  /**
   * Promueve la configuración probada a un plan real, previa confirmación.
   *
   * ★★ LA CONFIRMACIÓN NO ES CORTESÍA. Todo lo demás de esta pantalla es reversible de un botón: se
   * resetea y no queda nada. Ésta es la única acción del recorrido que sale del sandbox y crea algo en
   * el lado del dinero, y el usuario tiene que saber que a partir de aquí lo que toque ya no es de
   * mentira. El modal lo dice antes, no después.
   */
  async promote(): Promise<void> {
    // La configuración se lee ANTES: sus reglas salen del plan de práctica, que después se vacía.
    const config = await this.buildConfig();
    const loaded = this.loadedExperiment();
    const name = loaded?.name
      ?? this.store.status().plan?.name
      ?? this.translate.instant('GUIDED.EXPERIMENTS.DEFAULT_NAME');

    await this.store.promote();
    this.promoteOpen.set(false);

    const promoted = this.store.promoted();
    if (!promoted) {
      // El error queda también en la tarjeta, junto al botón, y el sandbox intacto.
      const error = this.store.promoteError();
      if (error) this.toasts.show(error, 'error');
      return;
    }

    // ★★ PROMOVER CIERRA LA PRUEBA. Antes el sandbox quedaba igual después de crear el plan real, con
    // el botón ofreciendo promover otra vez y el aviso de éxito escondido más abajo: un usuario creó
    // diez copias del mismo plan porque nada le dijo que ya estaba hecho. Ahora la prueba se guarda en
    // los experimentos (la que estaba cargada se actualiza), la pantalla se limpia y el aviso queda
    // arriba, con el enlace al plan real.
    // La huella de lo promovido viaja con el experimento: es lo que impide volver a crear este plan.
    const promotion: SandboxPromotionRecord = {
      planId: promoted.planId,
      planName: promoted.name,
      fingerprint: configFingerprint(config),
      promotedAt: new Date().toISOString(),
    };
    let savedAs: string | null = null;
    try {
      savedAs = (await this.store.saveExperiment(name, loaded?.id ?? null, config, promotion)).name;
    } catch {
      // El plan real ya existe: un fallo al guardar el experimento no lo deshace ni debe esconderlo.
      savedAs = null;
    }

    await this.clearScreen();
    this.store.promoted.set(promoted);
    this.promotedSavedAs.set(savedAs);
    // ★ EL TOAST ES LA CONFIRMACIÓN QUE SE VE SIEMPRE; el aviso de arriba queda con el enlace al plan.
    this.toasts.show('GUIDED.PROMOTE.TOAST_DONE', 'success', { name: promoted.name });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  /** El experimento donde quedó guardada la prueba promovida, para decirlo en el aviso. */
  readonly promotedSavedAs = signal<string | null>(null);

  private readonly plansApi = inject(PlansApiService);
  private readonly toasts = inject(ToastService);

  /** La configuración que se intentó promover ya está en producción: qué plan es y dónde avisarlo. */
  readonly duplicatePromotion = signal<{ record: SandboxPromotionRecord; where: 'card' | 'experiment' } | null>(null);
  readonly checkingPromotion = signal(false);

  /** La promoción del experimento abierto, para decir en el panel que ya está en producción. */
  readonly reviewedPromotion = computed(() => {
    const open = this.store.openExperiment();
    return open ? parseExperiment(open.snapshot).promotion : null;
  });

  /**
   * Abre la confirmación de promover, salvo que esta configuración ya esté en producción.
   *
   * ★★ SE COMPRUEBA ANTES DE PREGUNTAR. Promover dos veces lo mismo crea dos planes reales idénticos,
   * y nada lo impedía: un usuario creó diez. Si nada cambió desde la última promoción, no hay nada
   * nuevo que llevar a producción — se enseña el plan que ya existe en vez del botón de confirmar.
   */
  async requestPromote(): Promise<void> {
    this.duplicatePromotion.set(null);
    this.checkingPromotion.set(true);
    try {
      const existing = await this.livePromotionOf(await this.buildConfig());
      if (existing) {
        this.duplicatePromotion.set({ record: existing, where: 'card' });
      } else {
        this.promoteOpen.set(true);
      }
    } finally {
      this.checkingPromotion.set(false);
    }
  }

  /**
   * La promoción, todavía viva, de exactamente esta configuración, o `null` si se puede promover.
   *
   * ★ SE BUSCA EN TODOS LOS EXPERIMENTOS, no sólo en el cargado: la misma prueba rehecha a mano desde
   * cero también sería un duplicado. Y ★ SE PREGUNTA A PLANES SI EL PLAN SIGUE EXISTIENDO: si alguien lo
   * borró, volver a promoverlo no duplica nada. Un error que no sea «no existe» cuenta como que existe:
   * ante la duda, no se crea otro plan real.
   */
  private async livePromotionOf(config: SandboxExperimentConfig): Promise<SandboxPromotionRecord | null> {
    const fingerprint = configFingerprint(config);
    const matches = this.store.experiments()
      .map(e => parseExperiment(e.snapshot).promotion)
      .filter((p): p is SandboxPromotionRecord => p?.fingerprint === fingerprint);

    for (const promotion of matches) {
      const exists = await firstValueFrom(this.plansApi.getPlan(promotion.planId)).then(
        () => true,
        (err: { status?: number }) => err?.status !== 404,
      );
      if (exists) return promotion;
    }
    return null;
  }

  /** Los valores con que arranca el recorrido, para volver a ellos al limpiar la pantalla. */
  private readonly formDefaults = this.form.getRawValue();

  /** Vacía el sandbox y deja todos los formularios como al entrar. */
  private async clearScreen(): Promise<void> {
    await this.store.reset();
    this.form.reset(this.formDefaults);
    this.resetRuleForm();
    this.loadedExperiment.set(null);
    this.editMode.set(false);
    this.editRules.set([]);
    this.editRuleIndex.set(0);
    this.duplicatePromotion.set(null);
    this.asideTab.set('steps');
  }

  /** El formulario de regla vacío, con el nombre por defecto en el idioma del usuario. */
  private resetRuleForm(): void {
    this.ruleDef.tiersArray.clear();
    this.ruleDef.attainmentTiersArray.clear();
    this.ruleDef.conditionsArray.clear();
    this.ruleDef.form.reset();
    this.ruleDef.prepareForCreate(1);
    // Se espera a la traducción: `instant` antes de que cargue devolvería la clave y la regla se
    // llamaría «GUIDED.STEP.RULE.NAME_VALUE».
    void firstValueFrom(this.translate.get('GUIDED.STEP.RULE.NAME_VALUE')).then(ruleName => {
      if (!this.ruleDef.form.controls.name.value) this.ruleDef.form.controls.name.setValue(ruleName);
    });
  }

  async reset(): Promise<void> {
    this.editMode.set(false);
    this.resetting.set(true);
    try {
      await this.store.reset();
    } finally {
      this.resetting.set(false);
    }
  }
}
