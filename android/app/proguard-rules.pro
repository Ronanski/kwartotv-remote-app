# Shrink only: remove unused code (mostly the large Material icon set), but keep every name, so nothing that looks
# a class up by name (OkHttp, the QR scanner, Android itself) can break.
-dontobfuscate
-keep class com.kwartotv.** { *; }
-keepattributes *Annotation*, Signature, InnerClasses, EnclosingMethod, SourceFile, LineNumberTable
-dontwarn org.conscrypt.**
-dontwarn org.bouncycastle.**
-dontwarn org.openjsse.**
