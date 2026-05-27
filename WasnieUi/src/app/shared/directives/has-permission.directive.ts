import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { CurrentUserService } from '../../core/auth/current-user.service';

@Directive({
  selector: '[hasPermission]',
  standalone: true,
})
export class HasPermissionDirective {
  private readonly vcr = inject(ViewContainerRef);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly currentUser = inject(CurrentUserService);

  readonly hasPermission = input.required<string>();

  private hasView = false;

  constructor() {
    effect(() => {
      const allowed = this.currentUser.hasPermission(this.hasPermission());
      if (allowed && !this.hasView) {
        this.vcr.createEmbeddedView(this.templateRef);
        this.hasView = true;
      } else if (!allowed && this.hasView) {
        this.vcr.clear();
        this.hasView = false;
      }
    });
  }
}
