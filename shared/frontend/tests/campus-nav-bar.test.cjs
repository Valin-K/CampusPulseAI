const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const script = readFileSync(path.join(__dirname, '..', 'js', 'campus-nav-bar.js'), 'utf8');
const localConfig = JSON.parse(readFileSync(path.join(__dirname, '..', 'campus-config.json'), 'utf8'));
const sessionKey = 'campuspulse.auth.session';
const azureConfig = {
    UseSameOriginApi: true,
    Urls: Object.fromEntries(Object.keys(localConfig.Urls).map(name => [name, `https://${name.toLowerCase()}.example.azurecontainerapps.io/`])),
    AdditionalOrigins: []
};

async function boot(config, address, storage = new Map()) {
    const location = new URL(address);
    const events = {};
    const elements = new Map();
    const redirects = [];
    const alerts = [];
    class Element {
        constructor() { this.attributes = new Map(); }
        attachShadow() { this.shadowRoot = { innerHTML: '', querySelector: () => null }; }
        getAttribute(name) { return this.attributes.get(name); }
        hasAttribute(name) { return this.attributes.has(name); }
    }
    class Anchor extends Element {
        constructor(href) { super(); this.href = href; }
    }
    const window = {
        location: {
            href: location.href, origin: location.origin, port: location.port,
            pathname: location.pathname, search: location.search, hash: location.hash,
            assign: value => redirects.push(value), replace: value => redirects.push(value)
        },
        history: { state: null, replaceState: (_state, _title, value) => { window.cleanedUrl = value; } },
        dispatchEvent() {}, addEventListener() {}, removeEventListener() {}
    };
    const context = vm.createContext({
        window, URL, URLSearchParams, Event, HTMLElement: Element, HTMLAnchorElement: Anchor,
        sessionStorage: {
            getItem: key => storage.get(key) ?? null,
            setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key)
        },
        document: {
            addEventListener: (name, callback) => { events[name] = callback; },
            createElement: () => ({ setAttribute() {} }), body: { prepend: element => alerts.push(element) }
        },
        customElements: { get: name => elements.get(name), define: (name, type) => elements.set(name, type) },
        fetch: async () => ({ ok: true, json: async () => config }), console
    });
    vm.runInContext(script, context);
    await window.campusPulseWaitForNavigation();
    return {
        window, redirects, storage, elements, alerts,
        click(href, options = {}) {
            const anchor = new Anchor(href);
            const event = { button: 0, composedPath: () => [anchor], preventDefault() { this.defaultPrevented = true; }, ...options };
            events.click(event);
            return event;
        }
    };
}

for (const [environment, config] of [['local', localConfig], ['Azure', azureConfig]]) {
    test(`${environment}: session transfers to configured services, including index.html`, async () => {
        const storage = new Map([[sessionKey, 'example-session']]);
        const app = await boot(config, config.Urls.Portal, storage);
        for (const service of Object.keys(config.Urls).filter(name => name !== 'Portal')) {
            const event = app.click(`${config.Urls[service]}index.html`);
            assert.equal(event.defaultPrevented, true);
            const destination = new URL(app.redirects.at(-1));
            assert.equal(destination.origin, new URL(config.Urls[service]).origin);
            assert.equal(new URLSearchParams(destination.hash.slice(1)).get('campuspulse_session'), 'example-session');
        }
    });

    test(`${environment}: external, same-origin, and modified clicks do not transfer tokens`, async () => {
        const app = await boot(config, config.Urls.Portal, new Map([[sessionKey, 'private-session']]));
        app.click('https://external.example/');
        app.click('https://academicrecords.example.azurecontainerapps.io.attacker.example/');
        app.click(config.Urls.Portal);
        app.click(config.Urls.AcademicRecords, { ctrlKey: true });
        assert.deepEqual(app.redirects, []);
    });

    test(`${environment}: logout clears all six origins and finishes at the configured portal`, async () => {
        const stores = new Map(Object.values(config.Urls).map(address => [new URL(address).origin, new Map([
            [sessionKey, 'session'], ['campuspulse.demo-auth.session', 'legacy'], ['campuspulse.account.user-id', '42']
        ])]));
        const start = config.Urls.FinancialAidBilling;
        let app = await boot(config, start, stores.get(new URL(start).origin));
        app.window.campusPulseNavigation.signOutEverywhere();
        let destination = app.redirects.at(-1);
        let hops = 0;
        while (new URL(destination).searchParams.get('campuspulse_logout') === '1') {
            assert.ok(++hops <= 6, 'logout must terminate');
            app = await boot(config, destination, stores.get(new URL(destination).origin));
            destination = app.redirects.at(-1);
        }
        assert.equal(destination, config.Urls.Portal);
        for (const storage of stores.values()) assert.equal(storage.size, 0);
    });

    test(`${environment}: navigation and account links use configured destinations`, async () => {
        const app = await boot(config, config.Urls.Portal);
        const Navbar = app.elements.get('campus-nav-bar');
        const navbar = new Navbar();
        navbar.attributes.set('account-session', '');
        navbar.render();
        assert.ok(navbar.shadowRoot.innerHTML.includes(`href="${config.Urls.Portal}"`));
        assert.ok(navbar.shadowRoot.innerHTML.includes(`href="${config.Urls.AcademicRecords}login?returnUrl=`));
    });
}

test('incoming session is stored and removed from the visible URL', async () => {
    const session = { accessToken: 'test-token', expiresAt: '2099-01-01T00:00:00Z', user: { userId: 42 } };
    const fragment = new URLSearchParams({ campuspulse_session: JSON.stringify(session) });
    const app = await boot(azureConfig, `${azureConfig.Urls.CourseManagement}courses#${fragment}`);
    assert.deepEqual(JSON.parse(app.storage.get(sessionKey)), session);
    assert.equal(app.storage.get('campuspulse.account.user-id'), '42');
    assert.equal(app.window.cleanedUrl, '/courses');
});

test('logout query cannot redirect to an injected external destination', async () => {
    const app = await boot(azureConfig,
        `${azureConfig.Urls.AcademicRecords}?campuspulse_logout=1&campuspulse_logout_remaining=https://attacker.example`);
    assert.equal(new URL(app.redirects[0]).origin, new URL(azureConfig.Urls.Portal).origin);
});