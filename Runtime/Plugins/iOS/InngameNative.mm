// Inngame SDK — what Unity doesn't expose on iOS: the build number (CFBundleVersion) and the time zone name.
#import <Foundation/Foundation.h>

static char* InngameCopy(NSString* s)
{
    if (s == nil) return NULL;
    const char* u = [s UTF8String];
    char* r = (char*)malloc(strlen(u) + 1);
    strcpy(r, u);
    return r; // freed by the Mono marshaller
}

extern "C" char* InngameBuildNumber() { return InngameCopy([[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleVersion"]); }
extern "C" char* InngameTimeZone() { return InngameCopy([[NSTimeZone localTimeZone] name]); }
