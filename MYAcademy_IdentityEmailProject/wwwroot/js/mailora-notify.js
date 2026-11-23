/* =====================================================================
   Mailora — yeni mesaj bildirimleri (12.6)
   - Yoklama (polling): görünür sekmede 30 sn, arka planda 60 sn (karar 1-A, 2-A)
   - 15 dk etkinlik yoksa DURUR: arka plan istekleri oturumu uzatmasın (karar 4-A, 12.5)
   - Yeni mesaj: rozetler, zil noktası, sekme başlığı, toast, "N yeni mesaj" çubuğu,
     sekme arka plandaysa masaüstü bildirimi (karar 3-B, 5-A)
   Layout'taki window.Mailora (applyCounts, showToast, escapeHtml, inboxUrl) kullanılır.
   ===================================================================== */
(() => {
    'use strict';

    const M = window.Mailora;
    const bell = document.getElementById('notifBell');
    const cursorHost = document.querySelector('[data-notify-cursor]');
    if (!M || !bell || !cursorHost) return;          // giriş sayfaları gibi navbar'sız sayfalar

    /* ---------- Ayarlar ---------- */
    const VISIBLE_MS = 30_000;                 // sekme görünürken
    const HIDDEN_MS = 60_000;                  // sekme arka plandayken (masaüstü bildirimi için yine sorar)
    const IDLE_LIMIT_MS = 15 * 60_000;         // bu kadar süre etkinlik yoksa dur
    const MAX_BACKOFF_MS = 5 * 60_000;         // ağ hatalarında en uzun bekleme

    const CHECK_URL = bell.dataset.checkUrl;
    const MENU_URL = bell.dataset.menuUrl;
    const pip = document.getElementById('notifPip');
    const list = document.getElementById('notifList');
    const desktopBtn = document.getElementById('notifDesktop');
    const newBar = document.getElementById('newMailBar');          // sadece Gelen Kutusu sayfasında var
    const baseTitle = document.body.dataset.title ? `${document.body.dataset.title} · Mailora` : document.title;

    /* ---------- Durum ---------- */
    let cursor = Number(cursorHost.dataset.notifyCursor) || 0;     // sayfayla aynı anda üretildi (12.5, Kavram 2)
    let lastActivity = Date.now();
    let timer = null;
    let failures = 0;
    let stopped = false;          // oturum bitti (401): bir daha sorma
    let inFlight = false;         // aynı anda iki yoklama olmasın (ör. sekmeye dönüş + zamanlayıcı)
    let menuStale = false;        // zil listesi eskidi mi? (menü açılınca yenilenir)
    let pendingNew = 0;           // "N yeni mesaj" çubuğundaki sayı

    /* ---------- Okunmamış: zil noktası + sekme başlığı ---------- */
    function applyUnread(n) {
        if (pip) pip.hidden = n === 0;
        document.title = n > 0 ? `(${n}) ${baseTitle}` : baseTitle;
    }

    /* ---------- Zamanlama ---------- */
    const isIdle = () => Date.now() - lastActivity > IDLE_LIMIT_MS;

    function schedule(ms) {
        clearTimeout(timer);
        timer = setTimeout(check, ms);
    }

    // Hata olursa aralık her seferinde ikiye katlanır (sunucu zorlanıyorsa üstüne gitme)
    function nextDelay() {
        const base = document.hidden ? HIDDEN_MS : VISIBLE_MS;
        return failures === 0 ? base : Math.min(base * 2 ** failures, MAX_BACKOFF_MS);
    }

    /* ---------- Yoklama ---------- */
    async function check() {
        timer = null;
        if (stopped || inFlight) return;
        if (isIdle()) return;     // DUR: yeni bir etkinlik gelince onActivity yeniden başlatır

        inFlight = true;
        try {
            const response = await fetch(`${CHECK_URL}?after=${cursor}`, {
                headers: { 'Accept': 'application/json' },
                cache: 'no-store'
            });
            if (response.status === 401) {           // oturum bitmiş: sessizce dur, sonraki tıklama giriş sayfasına götürür
                stopped = true;
                return;                              // finally yine çalışır
            }
            if (!response.ok) throw new Error(String(response.status));

            const data = await response.json();
            failures = 0;
            cursor = data.cursor;
            M.applyCounts(data.counts);              // sidebar rozetleri: tek doğruluk kaynağı sunucu (7.8)
            applyUnread(data.counts.unreadInbox);
            if (data.newCount > 0) announce(data);
        } catch {
            failures++;
        } finally {
            inFlight = false;
        }

        if (!stopped) schedule(nextDelay());
    }

    /* ---------- Yeni mesaj geldi ---------- */
    function announce(data) {
        menuStale = true;
        const first = data.items[0];

        // showToast metni HTML olarak basar (innerHTML): ad ve konu KULLANICI VERİSİ → escapeHtml (8.5)
        const text = data.newCount === 1
            ? `<b>${M.escapeHtml(first.senderName)}</b> · ${M.escapeHtml(first.subject)}`
            : `<b>${data.newCount} yeni mesaj</b> · son: ${M.escapeHtml(first.senderName)}`;
        const target = data.newCount === 1 ? first.url : M.inboxUrl;
        M.showToast(text, 'bi-envelope-paper', { label: 'Aç', onClick: () => { location.href = target; } });

        // Gelen Kutusu açıksa liste kendiliğinden DEĞİŞMEZ; çubuk haber verir (karar 5-A)
        if (newBar) {
            pendingNew += data.newCount;
            newBar.querySelector('[data-count]').textContent = pendingNew;
            newBar.hidden = false;
        }

        // Sekme arka plandaysa masaüstü bildirimi (karar 3-B)
        if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
            showDesktop(data);
        }
    }

    function showDesktop(data) {
        data.items.slice(0, 3).forEach(item => {
            const options = {
                body: item.subject,                          // DÜZ METİN: tarayıcı HTML yorumlamaz, escape gerekmez
                tag: `mailora-${item.mailboxItemId}`         // aynı mesaj birden çok sekmeden gelirse tek bildirim
            };
            if (item.senderPhotoUrl) options.icon = item.senderPhotoUrl;

            const notification = new Notification(item.senderName, options);
            notification.onclick = () => {
                window.focus();
                location.href = item.url;
                notification.close();
            };
        });
    }

    /* ---------- Masaüstü bildirimi izni: sadece kullanıcının tıklamasıyla istenir ---------- */
    if (desktopBtn && 'Notification' in window && Notification.permission === 'default') {
        desktopBtn.hidden = false;
        desktopBtn.addEventListener('click', async () => {
            const result = await Notification.requestPermission();
            desktopBtn.hidden = true;
            M.showToast(result === 'granted' ? 'Masaüstü bildirimleri açıldı.' : 'Masaüstü bildirimlerine izin verilmedi.',
                result === 'granted' ? 'bi-bell' : 'bi-bell-slash');
        });
    }

    /* ---------- Zil listesi: menü AÇILIRKEN, sadece eskidiyse yenilenir (tembel yenileme) ---------- */
    bell.addEventListener('show.bs.dropdown', async () => {
        if (!menuStale || !list) return;
        try {
            const response = await fetch(MENU_URL, { cache: 'no-store' });
            if (response.ok) {
                list.innerHTML = await response.text();      // sunucuda Razor ile encode edilmiş HTML
                menuStale = false;
            }
        } catch { /* liste eski haliyle kalır, bir sonraki açılışta tekrar denenir */ }
    });

    // Sayfadaki işlemler (okundu, çöp, arşiv...) sayaçları değiştirdiğinde (7.8'deki olay)
    document.addEventListener('mailora:changed', e => {
        if (e.detail && e.detail.counts) {
            applyUnread(e.detail.counts.unreadInbox);
            menuStale = true;
        }
    });

    /* ---------- Etkinlik takibi (karar 4-A) ---------- */
    function onActivity() {
        const wasIdle = isIdle();
        lastActivity = Date.now();
        if (wasIdle && !stopped && timer === null) schedule(0);    // geri dönen kullanıcı: hemen sor
    }
    ['mousemove', 'keydown', 'click', 'scroll', 'touchstart'].forEach(type =>
        document.addEventListener(type, onActivity, { passive: true }));

    // Sekmeye dönmek de bir etkinliktir: beklemeden sor
    document.addEventListener('visibilitychange', () => {
        if (document.hidden || stopped) return;
        lastActivity = Date.now();
        schedule(0);
    });

    /* ---------- Başlangıç ---------- */
    applyUnread(Number(document.getElementById('badgeInbox')?.textContent) || 0);   // rozetle aynı sayı
    schedule(VISIBLE_MS);     // sayfa az önce taze veriyle çizildi: ilk soru 30 sn sonra
})();