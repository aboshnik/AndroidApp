# Add project specific ProGuard rules here.
# You can control the set of applied configuration files using the
# proguardFiles setting in build.gradle.
#
# For more details, see
#   http://developer.android.com/guide/developing/tools/proguard.html

# If your project uses WebView with JS, uncomment the following
# and specify the fully qualified class name to the JavaScript interface
# class:
#-keepclassmembers class fqcn.of.javascript.interface.for.webview {
#   public *;
#}

# Uncomment this to preserve the line number information for
# debugging stack traces.
#-keepattributes SourceFile,LineNumberTable

# If you keep the line number information, uncomment this to
# hide the original source file name.
#-renamesourcefileattribute SourceFile

# --- Retrofit / Gson / Kotlin safety rules (release minify) ---
# Keep signatures + runtime annotations required by Retrofit and Gson.
-keepattributes Signature,*Annotation*,InnerClasses,EnclosingMethod

# Keep Retrofit service interfaces and methods with HTTP annotations.
-keep interface com.example.app.api.*ApiService { *; }
-keepclasseswithmembers interface * {
    @retrofit2.http.* <methods>;
}

# Extra safety for Retrofit suspend + generic Response<T>.
-if interface * { @retrofit2.http.* <methods>; }
-keep,allowobfuscation interface <1>
-keep,allowobfuscation interface com.example.app.api.** { *; }
-keep class retrofit2.Response { *; }

# Keep API DTO models used by Gson.
-keep class com.example.app.api.** { *; }

# Keep fields annotated with @SerializedName.
-keepclassmembers,allowobfuscation class * {
    @com.google.gson.annotations.SerializedName <fields>;
}

# Keep TypeToken-based generic adapters (Gson).
-keep class com.google.gson.reflect.TypeToken { *; }
-keep class * extends com.google.gson.reflect.TypeToken

# Keep Kotlin metadata/coroutine continuation used by reflection/adapters.
-keep class kotlin.Metadata { *; }
-keep class kotlin.coroutines.Continuation { *; }

