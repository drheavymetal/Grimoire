import type { PushSubscriptionInput } from '../core/api/client';

// The web Web-Push adapter (feature B17). This is the ONLY place that touches the Service Worker,
// PushManager and Notification APIs (invariant 6 — core stays DOM-free; a native build would swap
// this for expo-notifications). It registers /sw.js, subscribes with the server's VAPID key, and
// flattens the browser subscription into the shape the API stores.

const SERVICE_WORKER_URL = '/sw.js';

// Whether this browser can do Web Push at all (a plain http page or an old browser cannot).
export function isPushSupported(): boolean {
  return (
    typeof navigator !== 'undefined' &&
    'serviceWorker' in navigator &&
    typeof window !== 'undefined' &&
    'PushManager' in window &&
    'Notification' in window
  );
}

// The current OS notification permission, or 'default' when unsupported.
export function pushPermission(): NotificationPermission {
  if (typeof Notification === 'undefined') {
    return 'default';
  }
  return Notification.permission;
}

async function ensureRegistration(): Promise<ServiceWorkerRegistration> {
  const existing = await navigator.serviceWorker.getRegistration();
  if (existing) {
    return existing;
  }
  return navigator.serviceWorker.register(SERVICE_WORKER_URL);
}

// Subscribes this browser to push with the server's VAPID public key. Returns the subscription
// flattened for the API, or null if the user denied permission. Throws only on unexpected errors.
export async function subscribePush(vapidPublicKey: string): Promise<PushSubscriptionInput | null> {
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') {
    return null;
  }

  const registration = await ensureRegistration();
  await navigator.serviceWorker.ready;

  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
  });

  return flatten(subscription);
}

// The existing subscription for this browser, flattened, or null if not subscribed.
export async function existingPushSubscription(): Promise<PushSubscriptionInput | null> {
  const registration = await navigator.serviceWorker.getRegistration();
  if (!registration) {
    return null;
  }
  const subscription = await registration.pushManager.getSubscription();
  return subscription ? flatten(subscription) : null;
}

// Unsubscribes this browser. Returns the (now-removed) subscription so the caller can drop it
// server-side, or null if there was nothing to unsubscribe.
export async function unsubscribePush(): Promise<PushSubscriptionInput | null> {
  const registration = await navigator.serviceWorker.getRegistration();
  if (!registration) {
    return null;
  }
  const subscription = await registration.pushManager.getSubscription();
  if (!subscription) {
    return null;
  }
  const flat = flatten(subscription);
  await subscription.unsubscribe();
  return flat;
}

function flatten(subscription: PushSubscription): PushSubscriptionInput {
  const p256dh = subscription.getKey('p256dh');
  const auth = subscription.getKey('auth');
  if (!p256dh || !auth) {
    throw new Error('Push subscription is missing its encryption keys.');
  }
  return {
    endpoint: subscription.endpoint,
    p256dh: arrayBufferToBase64Url(p256dh),
    auth: arrayBufferToBase64Url(auth),
  };
}

// VAPID keys travel as base64url; PushManager wants the raw bytes as a BufferSource. The buffer is
// allocated explicitly (not the ArrayBufferLike overload) so it types as a plain ArrayBuffer view.
function urlBase64ToUint8Array(base64Url: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64Url.length % 4)) % 4);
  const base64 = (base64Url + padding).replace(/-/g, '+').replace(/_/g, '/');
  const raw = window.atob(base64);
  const output = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) {
    output[i] = raw.charCodeAt(i);
  }
  return output;
}

function arrayBufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return window.btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
