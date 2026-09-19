import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { UsersStore } from './users.store';
import { UsersApiService } from '../services/users.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { RolePermissions, TenantUsersResponse } from '../models/user.model';

/**
 * Role simplification (KAN-92/KAN-99): the pickers offer only TenantAdmin and Sales rep.
 *
 * ★★ THE FIXTURE HAS THE SHAPE OF `GET /api/users/roles` — every role, each with `assignable` — and
 * the hidden two are IN it. A fixture that simply left CompManager and Manager out would pass whether
 * or not the store filters anything, which is the dead green this repo has a rule about (§A2).
 */
const ROLES: RolePermissions[] = [
  { role: 'TenantAdmin', permissions: ['Users.Manage'], assignable: true },
  { role: 'CompManager', permissions: ['Plans.Read'], assignable: false },
  { role: 'Manager', permissions: ['Payees.Read'], assignable: false },
  { role: 'Rep', permissions: ['LedgerSummary.Read'], assignable: true },
];

const EMPTY_USERS = { users: [], invitations: [], seats: null } as unknown as TenantUsersResponse;

describe('UsersStore — role picker options', () => {
  let store: UsersStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: UsersApiService,
          useValue: {
            getUsers: () => of(EMPTY_USERS),
            rolePermissions: () => of(ROLES),
          },
        },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['success', 'error']) },
      ],
    });
    store = TestBed.inject(UsersStore);
  });

  it('offers only the roles the server marks assignable, in the server order', async () => {
    await store.load();
    await Promise.resolve();

    expect(store.roleOptions().map(o => o.value)).toEqual(['TenantAdmin', 'Rep']);
  });

  it('labels them through the role whitelist, never by concatenating the name (§C2)', async () => {
    await store.load();
    await Promise.resolve();

    expect(store.roleOptions().map(o => o.label))
      .toEqual(['USERS.ROLES.TENANT_ADMIN', 'USERS.ROLES.REP']);
  });

  it('still describes a hidden role for the access panel', async () => {
    await store.load();
    await Promise.resolve();

    expect(store.permissionsFor('CompManager').has('Plans.Read')).toBeTrue();
  });

  it('offers nothing before the roles have loaded, rather than a guessed list', () => {
    expect(store.roleOptions()).toEqual([]);
  });
});
