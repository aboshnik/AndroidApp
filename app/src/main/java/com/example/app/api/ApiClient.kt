package com.example.app.api

import com.example.app.BuildConfig
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

object ApiClient {
    private val normalizedBaseUrl: String by lazy {
        val baseUrl = BuildConfig.API_BASE_URL.trim()
        when {
            baseUrl.isEmpty() -> "http://10.0.2.2:5000/"
            baseUrl.endsWith("/") -> baseUrl
            else -> "$baseUrl/"
        }
    }

    private val retrofit: Retrofit by lazy {
        Retrofit.Builder()
            .baseUrl(normalizedBaseUrl)
            .client(createOkHttpClient())
            .addConverterFactory(GsonConverterFactory.create())
            .build()
    }

    val employeeApi: EmployeeApiService by lazy {
        retrofit.create(EmployeeApiService::class.java)
    }

    val postApi: PostApiService by lazy {
        retrofit.create(PostApiService::class.java)
    }

    val notificationsApi: NotificationsApiService by lazy {
        retrofit.create(NotificationsApiService::class.java)
    }

    val authApi: AuthApiService by lazy {
        retrofit.create(AuthApiService::class.java)
    }

    val chatApi: ChatApiService by lazy {
        retrofit.create(ChatApiService::class.java)
    }

    val securityApi: SecurityApiService by lazy {
        retrofit.create(SecurityApiService::class.java)
    }

    val appUpdateApi: AppUpdateApiService by lazy {
        retrofit.create(AppUpdateApiService::class.java)
    }

    val pushTokenApi: PushTokenApiService by lazy {
        retrofit.create(PushTokenApiService::class.java)
    }

    private val adminRetrofit: Retrofit by lazy {
        Retrofit.Builder()
            .baseUrl(normalizedBaseUrl)
            .client(createAdminOkHttpClient())
            .addConverterFactory(GsonConverterFactory.create())
            .build()
    }

    val developerApi: DeveloperApiService by lazy {
        adminRetrofit.create(DeveloperApiService::class.java)
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

