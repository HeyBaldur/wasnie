import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SessionRefreshService {
  isRefreshing = false;
  private readonly subject = new Subject<string | null>();

  waitForToken$(): Observable<string | null> {
    return this.subject.asObservable();
  }

  broadcast(token: string | null): void {
    this.subject.next(token);
  }
}
