package com.example.app.api

import android.content.Context
import com.example.app.BuildConfig
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

object ApiClient {
    @Volatile
    private var appContext: Context? = null

    @Volatile
    private var baseUrl: String = normalizeBuildBaseUrl()

    @Volatile
    private var retrofit: Retrofit = buildRetrofit(baseUrl)

    @Volatile
    private var adminRetrofit: Retrofit = buildAdminRetrofit(baseUrl)

    fun init(context: Context) {
        appContext = context.applicationContext
        refreshIfNeeded()
    }

    fun refreshIfNeeded() {
        val ctx = appContext ?: return
        val nextBaseUrl = RuntimeApiConfig.resolveBaseUrl(ctx, BuildConfig.API_BASE_URL)
        if (nextBaseUrl.equals(baseUrl, ignoreCase = true)) return
        synchronized(this) {
            if (nextBaseUrl.equals(baseUrl, ignoreCase = true)) return
            baseUrl = nextBaseUrl
            retrofit = buildRetrofit(nextBaseUrl)
            adminRetrofit = buildAdminRetrofit(nextBaseUrl)
        }
    }

    fun currentBaseUrl(): String = baseUrl

    val employeeApi: EmployeeApiService get() = retrofit.create(EmployeeApiService::class.java)
    val postApi: PostApiService get() = retrofit.create(PostApiService::class.java)
    val notificationsApi: NotificationsApiService get() = retrofit.create(NotificationsApiService::class.java)
    val authApi: AuthApiService get() = retrofit.create(AuthApiService::class.java)
    val chatApi: ChatApiService get() = retrofit.create(ChatApiService::class.java)
    val coinsApi: CoinsApiService get() = retrofit.create(CoinsApiService::class.java)
    val securityApi: SecurityApiService get() = retrofit.create(SecurityApiService::class.java)
    val pushTokenApi: PushTokenApiService get() = retrofit.create(PushTokenApiService::class.java)
    val developerApi: DeveloperApiService get() = adminRetrofit.create(DeveloperApiService::class.java)

    private fun normalizeBuildBaseUrl(): String {
        val base = RuntimeApiConfig.normalizeOrNull(BuildConfig.API_BASE_URL)
        return base ?: "http://10.0.2.2:5000/"
    }

    private fun buildRetrofit(base: String): Retrofit {
        return Retrofit.Builder()
            .baseUrl(base)
            .client(createOkHttpClient())
            .addConverterFactory(GsonConverterFactory.create())
            .build()
    }

    private fun buildAdminRetrofit(base: String): Retrofit {
        return Retrofit.Builder()
            .baseUrl(base)
            .client(createAdminOkHttpClient())
            .addConverterFactory(GsonConverterFactory.create())
            .build()
    }

    private fun createOkHttpClient(): OkHttpClient {
        val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BODY
        }
        return OkHttpClient.Builder()
            .addInterceptor(logging)
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()
    }

    private fun createAdminOkHttpClient(): OkHttpClient {
        val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BODY
        }
        return OkHttpClient.Builder()
            .addInterceptor { chain ->
                val req = chain.request().newBuilder()
                    .addHeader("X-Admin-Key", BuildConfig.ADMIN_API_KEY)
                    .build()
                chain.proceed(req)
            }
            .addInterceptor(logging)
            .connectTimeout(30, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()
    }
}

