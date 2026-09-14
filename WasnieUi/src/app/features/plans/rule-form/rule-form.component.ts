import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractApiError, extractApiErrorCode } from '../../../shared/utils/api-error';
import { isKnownRateTableError, rateTableErrorKey, rateTableErrorParams } from './rate-table-error';
import { TranslateModule, TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { getPlanPermissions } from '../services/plan-permissions';
import { AddRuleRequest, UpdateRuleRequest } from '../models/rule.model';
import {
  WsPageHeaderComponent,
  WsButtonComponent,
  WsEmptyStateComponent,
} from '../../../shared/ui';
import { createRuleDefinitionForm } from './rule-definition-form';
import { RuleDefinitionFieldsComponent } from './rule-definition-fields.component';
import { RulePreviewComponent } from './rule-preview.component';

/**
 * The Plans rule page: loads the plan, decides whether the rule may be edited, and saves it.
 *
 * ★ THE DEFINITION ITSELF IS NOT HERE. The fields, their behaviour and the simulator live in
 * {@link createRuleDefinitionForm}, rendered by `app-rule-definition-fields` and `app-rule-preview`,
 * because the guided tour's sandbox edits the very same definition. This component owns only what is
 * specific to a real plan: the route, the plan's status, the missing-rule state and the save.
 */
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
    WsPageHeaderComponent,
    WsButtonComponent,
    WsEmptyStateComponent,
    RuleDefinitionFieldsComponent,
    RulePreviewComponent,
  ],
  templateUrl: './rule-form.component.html',
  styleUrl: './rule-form.component.scss',
})
export class RuleFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly plansApi = inject(PlansApiService);

  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly ruleId = this.route.snapshot.paramMap.get('ruleId') ?? null;
  readonly isEdit = !!this.ruleId;
  readonly saving = signal(false);
  readonly readOnly = signal(false);

  readonly planCurrency = computed(() => this.store.selectedPlan()?.currency ?? 'USD');

  readonly def = createRuleDefinitionForm({
    planId: () => this.planId,
    currency: this.planCurrency,
    readOnly: this.readOnly,
    simulate: (request) => this.plansApi.simulateRule(this.planId, request),
  });

  get form() {
    return this.def.form;
  }

  ngOnInit(): void {
    this.def.loadCatalogs();

    const loadPromise = (!this.store.selectedPlan() || this.store.selectedPlan()?.id !== this.planId)
      ? this.store.loadPlan(this.planId)
      : Promise.resolve();

    loadPromise.then(() => {
      if (this.isEdit) {
        this._loadExistingRule();  // populate signals while form is still enabled
      } else {
        // Default the sort order to max(existing) + 1 so new rules don't all land on #1.
        // Sort order is presentation-only (it does not affect payout amounts), so no
        // uniqueness validation is needed — this just avoids silent collisions by default.
        const existingRules = this.store.selectedPlan()?.rules ?? [];
        const nextSortOrder = existingRules.length > 0
          ? Math.max(...existingRules.map((r) => r.sortOrder)) + 1
          : 1;
        this.def.prepareForCreate(nextSortOrder);
      }

      const perms = getPlanPermissions(this.store.selectedPlan()?.status);
      if (!perms.canEditRule) {
        this.form.disable({ emitEvent: false });  // disable after load, no extra emission
        this.readOnly.set(true);
      }
    });
  }

  /**
   * The rule's id AS THE SERVER SPELLS IT, resolved once the plan has loaded.
   *
   * ★ NOT THE ONE FROM THE URL. The route value is whatever was typed or pasted, and the API emits
   * GUIDs in lower case; saving with the URL's casing would send an id the server may not match. Null
   * until the rule is found, and it stays null when there is none.
   */
  private _resolvedRuleId: string | null = null;

  /**
   * ★★ THE RULE THE URL NAMES DOES NOT EXIST. An explicit state, because the alternative was silence:
   * a `return` here left a pristine, fully enabled Add-a-rule form on screen under an "Edit rule"
   * heading, with no error of any kind. Saving it would have created a second rule.
   *
   * ★ AND IT IS NOT THE CASE-SENSITIVITY FIX. Comparing ids case-insensitively (below) stops the
   * commonest way of reaching this state — a GUID pasted in upper case — but a genuinely deleted or
   * mistyped rule reaches it too, and that one no amount of normalising can fix. The symptom of the
   * blank form is also indistinguishable from what a validation leaking into the read path would
   * produce, which is exactly the diagnosis this WI had to rule out by hand.
   */
  readonly ruleNotFound = signal(false);

  private _loadExistingRule(): void {
    const plan = this.store.selectedPlan();

    // A plan that never loaded is a different failure with its own error surface; saying "rule not
    // found" about it would name the wrong thing.
    if (!plan) return;

    // ★ CASE-INSENSITIVE ON PURPOSE. The API emits GUIDs lower-cased, so a link carrying
    // "…-A1B2" — from a copy out of the database, a log line, or an email — matched nothing.
    const wanted = this.ruleId?.toLowerCase();
    const rule = plan.rules.find((r) => r.id.toLowerCase() === wanted);

    if (!rule) {
      this.ruleNotFound.set(true);
      this.form.disable({ emitEvent: false });
      return;
    }

    this._resolvedRuleId = rule.id;
    this.def.patchFromRule(rule);
  }

  async onSubmit(): Promise<void> {
    if (this.readOnly()) return;
    // There is nothing to update. Saving here used to CREATE a second rule from a form the user
    // believed they were editing.
    if (this.ruleNotFound()) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      // Never fail silently: tell the user why nothing happened on Save.
      this.toast.show('PLANS.TOAST_RULE_INVALID', 'error');
      return;
    }
    this.saving.set(true);
    try {
      const request: AddRuleRequest = this.def.buildDefinition();

      if (this.isEdit) {
        // The resolved id, not the URL's: see _resolvedRuleId. It is non-null here because
        // onSubmit returns early while ruleNotFound is set.
        const id = this._resolvedRuleId!;
        await this.store.updateRule(this.planId, id, { ...request, ruleId: id } as UpdateRuleRequest);
        this.toast.show('PLANS.TOAST_RULE_UPDATED', 'success');
      } else {
        await this.store.addRule(this.planId, request);
        this.toast.show('PLANS.TOAST_RULE_ADDED', 'success');
      }
      this.router.navigate(['/plans', this.planId]);
    } catch (err) {
      this._showSaveError(err);
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * ★★ THE SIX LADDER REFUSALS ARRIVE AS A CODE, AND THIS IS WHERE THEY BECOME A SENTENCE.
   *
   * They used to arrive as English prose built in C# and were painted into the toast unchanged, so
   * the Spanish and Polish builds showed an English sentence — and fixing a wording meant redeploying
   * the backend. The server now sends `{ code, parameters }`; the wording lives in the three
   * translation files, and `rateTableErrorKey` maps the code through an explicit whitelist so an
   * unknown one degrades to a generic line instead of printing a raw identifier.
   *
   * ★ A CODED ERROR THIS BUILD DOES NOT RECOGNISE IS NOT ASSUMED TO BE A RATE-TABLE PROBLEM. It falls
   * through to the plain message path, exactly as before, rather than being described as a bad
   * ladder — a wrong explanation is worse than a vague one on the screen that decides what people
   * are paid.
   */
  private _showSaveError(err: unknown): void {
    const coded = extractApiErrorCode(err);

    if (coded && isKnownRateTableError(coded)) {
      this.toast.show(rateTableErrorKey(coded), 'error', rateTableErrorParams(coded));
      return;
    }

    this.toast.show(extractApiError(err), 'error');
  }
}
