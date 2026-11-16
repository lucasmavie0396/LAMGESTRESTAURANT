package com.lamgest.venda

import android.app.Application
import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest

/**
 * O servidor está na rede Wi-Fi local, que pode não ter internet. Nesse caso o Android passa o tráfego para os dados móveis
 * e o servidor deixa de se ver: por omissão a app fica presa ao Wi-Fi, tenha ou não internet.
 * Fora do restaurante (acesso pela VPN) a app solta-se do Wi-Fi para o tráfego seguir pela VPN: ver [prenderAoWifi].
 */
class LamgestApp : Application() {
    override fun onCreate() {
        super.onCreate()
        redes = getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
        val pedido = NetworkRequest.Builder()
            .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
            .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
            .build()
        redes?.requestNetwork(pedido, object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) {
                wifi = network
                if (presoAoWifi) redes?.bindProcessToNetwork(network)
            }

            override fun onLost(network: Network) {
                if (wifi == network) wifi = null
                redes?.bindProcessToNetwork(null)
            }
        })
    }

    companion object {
        @Volatile private var redes: ConnectivityManager? = null
        @Volatile private var wifi: Network? = null
        @Volatile private var presoAoWifi = true

        /** true: tráfego pelo Wi-Fi (servidor na rede local). false: rede predefinida do Android (dados móveis/VPN). */
        fun prenderAoWifi(prender: Boolean) {
            presoAoWifi = prender
            redes?.bindProcessToNetwork(if (prender) wifi else null)
        }
    }
}
