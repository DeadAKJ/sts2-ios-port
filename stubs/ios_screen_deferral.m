#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <objc/runtime.h>

static void STS2_SwizzleInstanceMethod(Class cls, SEL origSel, SEL swizSel) {
    Method origMethod = class_getInstanceMethod(cls, origSel);
    Method swizMethod = class_getInstanceMethod(cls, swizSel);
    if (!swizMethod) return;

    BOOL didAdd = class_addMethod(cls, origSel, method_getImplementation(swizMethod), method_getTypeEncoding(swizMethod));
    if (didAdd) {
        if (origMethod) {
            class_replaceMethod(cls, swizSel, method_getImplementation(origMethod), method_getTypeEncoding(origMethod));
        }
    } else if (origMethod) {
        method_exchangeImplementations(origMethod, swizMethod);
    }
}

@interface UIViewController (STS2ScreenEdgeProtectCategory)
- (UIRectEdge)sts2_preferredScreenEdgesDeferringSystemGestures;
- (BOOL)sts2_prefersHomeIndicatorAutoHidden;
- (BOOL)sts2_prefersStatusBarHidden;
- (void)sts2_viewDidAppear:(BOOL)animated;
@end

@implementation UIViewController (STS2ScreenEdgeProtectCategory)

- (UIRectEdge)sts2_preferredScreenEdgesDeferringSystemGestures {
    // UIRectEdgeAll = Top | Left | Bottom | Right (15)
    // Deferring all edges ensures swiping up from bottom (e.g. card dragging) requires a 2nd swipe to trigger system gestures
    return UIRectEdgeAll;
}

- (BOOL)sts2_prefersHomeIndicatorAutoHidden {
    // Auto-hide the home bar indicator when inactive so it dims and does not intercept touch drags
    return YES;
}

- (BOOL)sts2_prefersStatusBarHidden {
    return YES;
}

- (void)sts2_viewDidAppear:(BOOL)animated {
    [self sts2_viewDidAppear:animated];
    [self setNeedsUpdateOfScreenEdgesDeferringSystemGestures];
    [self setNeedsUpdateOfHomeIndicatorAutoHidden];
    [self setNeedsStatusBarAppearanceUpdate];
}

@end

__attribute__((visibility("default")))
@interface STS2ScreenEdgeInitializer : NSObject
@end

@implementation STS2ScreenEdgeInitializer

+ (void)load {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        Class cls = [UIViewController class];
        STS2_SwizzleInstanceMethod(cls, @selector(preferredScreenEdgesDeferringSystemGestures), @selector(sts2_preferredScreenEdgesDeferringSystemGestures));
        STS2_SwizzleInstanceMethod(cls, @selector(prefersHomeIndicatorAutoHidden), @selector(sts2_prefersHomeIndicatorAutoHidden));
        STS2_SwizzleInstanceMethod(cls, @selector(prefersStatusBarHidden), @selector(sts2_prefersStatusBarHidden));
        STS2_SwizzleInstanceMethod(cls, @selector(viewDidAppear:), @selector(sts2_viewDidAppear:));
        NSLog(@"[STS2EdgeProtect] Installed screen edge deferral & home indicator auto-hide on UIViewController.");
    });
}

@end
