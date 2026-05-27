import { Pipe, PipeTransform, inject } from '@angular/core';
import { CurrentUserService } from '../../core/auth/current-user.service';

@Pipe({
  name: 'hasPermission',
  standalone: true,
  pure: false,
})
export class HasPermissionPipe implements PipeTransform {
  private readonly currentUser = inject(CurrentUserService);

  transform(permission: string): boolean {
    return this.currentUser.hasPermission(permission);
  }
}
