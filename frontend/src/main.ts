import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { isDevMode } from '@angular/core';
import { AppComponent } from './app/app.component';
import { installScreenWakeLock } from './wake-lock';

installScreenWakeLock();

bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(),
    provideRouter([]),
    provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' })
  ]
}).catch(err => console.error(err));
