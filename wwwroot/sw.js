// Service Worker - Block Stripe wallet-config requests
self.addEventListener('install', function(event) {
    self.skipWaiting();
});

self.addEventListener('activate', function(event) {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', function(event) {
    var url = event.request.url;
    
    // Block wallet-config requests - return success immediately
    if (url.indexOf('wallet-config') !== -1 || url.indexOf('merchant-ui-api.stripe.com') !== -1) {
        event.respondWith(
            new Response(JSON.stringify({}), {
                status: 200,
                statusText: 'OK',
                headers: {
                    'Content-Type': 'application/json',
                    'Access-Control-Allow-Origin': '*',
                    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
                    'Access-Control-Allow-Headers': 'Content-Type'
                }
            })
        );
        return;
    }
    
    event.respondWith(fetch(event.request));
});

