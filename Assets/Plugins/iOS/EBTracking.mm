// App Tracking Transparency für Elemental Buddies (C#: IosTracking.cs, iOS-Plan 2026-11-15).
// Framework AppTrackingTransparency wird schwach gelinkt (IosBuildPostProcess). Funktioniert mit und ohne ARC.
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

typedef void (*EBTrackingCallback)(int status);

// Status wie ATTrackingManagerAuthorizationStatus (0 offen, 1 eingeschränkt, 2 abgelehnt, 3 erlaubt)
static int EBTrackingStatus(void)
{
    if (@available(iOS 14, *))
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    return 3; // vor iOS 14 gibt es keine Abfrage
}

static void EBTrackingAnswer(EBTrackingCallback cb, int status)
{
    if (cb == NULL) return;
    dispatch_async(dispatch_get_main_queue(), ^{ cb(status); });
}

static void EBTrackingAskNow(EBTrackingCallback cb)
{
    if (@available(iOS 14, *))
    {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            EBTrackingAnswer(cb, (int)status);
        }];
        return;
    }
    EBTrackingAnswer(cb, 3);
}

extern "C" {

int EBTracking_GetStatus(void)
{
    return EBTrackingStatus();
}

// Dialog nur, wenn die App aktiv ist (sonst zeigt iOS ihn nicht) – sonst bis didBecomeActive warten
void EBTracking_Request(EBTrackingCallback cb)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        int status = EBTrackingStatus();
        if (status != 0) { EBTrackingAnswer(cb, status); return; }
        if ([UIApplication sharedApplication].applicationState == UIApplicationStateActive) { EBTrackingAskNow(cb); return; }
        __block id token = nil;
        token = [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidBecomeActiveNotification
                                                                  object:nil
                                                                   queue:[NSOperationQueue mainQueue]
                                                              usingBlock:^(NSNotification *note) {
            if (token == nil) return;
            [[NSNotificationCenter defaultCenter] removeObserver:token];
            token = nil;
            EBTrackingAskNow(cb);
        }];
    });
}

}
