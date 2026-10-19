import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { currentUserUrl, firmsUrl } from './current-user';
import { CurrentUserService } from './current-user.service';
import { aFirm, aUser } from './test-data';

describe('CurrentUserService', () => {
  let service: CurrentUserService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CurrentUserService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads the user once, even when asked several times at once', async () => {
    const first = service.load();
    const second = service.load();
    http.expectOne(currentUserUrl).flush(aUser());

    expect(await first).toEqual(aUser());
    expect(await second).toEqual(aUser());
    expect(await service.load()).toEqual(aUser());
    expect(service.firm()).toEqual(aFirm);
  });

  it('retries on the next call when loading fails', async () => {
    const failed = service.load();
    http.expectOne(currentUserUrl).flush(null, { status: 500, statusText: 'Server Error' });
    await expect(failed).rejects.toBeDefined();

    const retried = service.load();
    http.expectOne(currentUserUrl).flush(aUser());
    expect(await retried).toEqual(aUser());
  });

  it('knows the firm right after creating it', async () => {
    const loaded = service.load();
    http.expectOne(currentUserUrl).flush(aUser(null));
    await loaded;
    expect(service.firm()).toBeNull();

    const created = service.createFirm('Gestoría López');
    const request = http.expectOne(firmsUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Gestoría López' });
    request.flush(aFirm);

    expect(await created).toEqual(aFirm);
    expect(service.firm()).toEqual(aFirm);
  });

  it('asks the API again after a reset', async () => {
    const loaded = service.load();
    http.expectOne(currentUserUrl).flush(aUser(null));
    await loaded;

    service.reset();
    const reloaded = service.load();
    http.expectOne(currentUserUrl).flush(aUser());

    expect((await reloaded).firm).toEqual(aFirm);
  });
});
