// Grimoire Service Worker — Web Push for the Weekly Rite (feature B17).
//
// It shows the OS notification for an incoming push and focuses/opens the app on click. The push
// payload is a small data object ({ type, count, url }); the copy is composed here from
// navigator.language so the notification is bilingual (es/en) without shipping i18next into the
// worker — the app's i18next runs in the page, not here.

const COPY = {
  es: {
    weeklyTitle: 'Tu Rito Semanal',
    weeklyBody: (n) => `${n} bandas te esperan, a ciegas.`,
    fallbackTitle: 'Grimoire',
    fallbackBody: 'Hay algo nuevo para ti.',
  },
  en: {
    weeklyTitle: 'Your Weekly Rite',
    weeklyBody: (n) => `${n} bands await, blind.`,
    fallbackTitle: 'Grimoire',
    fallbackBody: 'Something new awaits you.',
  },
};

function lang() {
  const l = (self.navigator && self.navigator.language ? self.navigator.language : 'en').toLowerCase();
  return l.startsWith('es') ? 'es' : 'en';
}

self.addEventListener('push', (event) => {
  const copy = COPY[lang()];

  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch (_err) {
    data = {};
  }

  const isWeekly = data.type === 'weekly';
  const title = isWeekly ? copy.weeklyTitle : copy.fallbackTitle;
  const body = isWeekly ? copy.weeklyBody(data.count || 7) : copy.fallbackBody;
  const url = data.url || '/weekly';

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      tag: isWeekly ? 'grimoire-weekly' : 'grimoire',
      data: { url },
    }),
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const target = (event.notification.data && event.notification.data.url) || '/weekly';

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if ('focus' in client) {
          client.navigate(target);
          return client.focus();
        }
      }
      return self.clients.openWindow(target);
    }),
  );
});
