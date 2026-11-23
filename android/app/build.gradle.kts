plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Assinatura: com os segredos ANDROID_KEYSTORE_* o APK pode actualizar-se por cima do anterior; sem eles usa a chave de depuração.
val ficheiroChave = System.getenv("ANDROID_KEYSTORE_FICHEIRO")?.let { file(it) }?.takeIf { it.exists() }

android {
    namespace = "com.lamgest.venda"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.lamgest.venda"
        // Android 5.0 ou mais recente (tablets e PDA antigos). O ecrã de venda precisa do Android System WebView 70 ou mais
        // (a app avisa); o leitor de QR (zxing 3.4+) usa APIs de Java 8, dadas pelo desugaring abaixo.
        minSdk = 21
        targetSdk = 34
        versionCode = (System.getenv("GITHUB_RUN_NUMBER") ?: "1").toInt()
        versionName = "1.0." + (System.getenv("GITHUB_RUN_NUMBER") ?: "0")
    }

    signingConfigs {
        if (ficheiroChave != null) {
            create("lamgest") {
                storeFile = ficheiroChave
                storePassword = System.getenv("ANDROID_KEYSTORE_SENHA")
                keyAlias = System.getenv("ANDROID_KEYSTORE_ALIAS")
                keyPassword = System.getenv("ANDROID_KEYSTORE_SENHA")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (ficheiroChave != null) signingConfigs.getByName("lamgest") else signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        isCoreLibraryDesugaringEnabled = true
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
    implementation("androidx.activity:activity-ktx:1.9.3")
    implementation("com.sunmi:printerlibrary:1.0.24")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
}
