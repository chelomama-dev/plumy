import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { currentUserUrl } from './current-user';
import { CurrentUserService } from './current-user.service';
import { noFirmInterceptor, noFirmProblemType } from './no-firm.interceptor';
import { aUser } from './test-data';

describe('noFirmInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([noFirmInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const loaded = TestBed.inject(CurrentUserService).load();
    backend.expectOne(currentUserUrl).flush(aUser());
    await loaded;
  });

  afterEach(() => backend.verify());

  async function failWith(status: number, body: object | null) {
    const response = firstValueFrom(http.get('/api/client-companies'));
    backend.expectOne('/api/client-companies').flush(body, { status, statusText: 'Error' });
    await expect(response).rejects.toMatchObject({ status });
  }

  it('sends a user who lost their firm to onboarding, forgetting the cached firm', async () => {
    await failWith(403, { type: noFirmProblemType, title: 'No accounting firm' });

    expect(navigate).toHaveBeenCalledWith('/onboarding');
    expect(TestBed.inject(CurrentUserService).firm()).toBeNull();
  });

  it('leaves other 403s alone (e.g. a member on an owner-only endpoint)', async () => {
    await failWith(403, { title: 'Owner only' });

    expect(navigate).not.toHaveBeenCalled();
    expect(TestBed.inject(CurrentUserService).firm()).not.toBeNull();
  });

  it('leaves other errors alone', async () => {
    await failWith(500, null);

    expect(navigate).not.toHaveBeenCalled();
  });
});
