#include "pch.h"
#include "EosPatch.h"

namespace Eos
{
	EosPatch::EosPatch(Platform::String^ channel, Platform::String^ part, Platform::String^ type, Platform::String^ universe, Platform::String^ address)
	{
		m_Channel = std::wcstol(channel->Data(), nullptr, 10);
		m_Part = std::wcstol(part->Data(), nullptr, 10);
		m_Universe = std::wcstol(universe->Data(), nullptr, 10);
		m_Address = std::wcstol(address->Data(), nullptr, 10);
		m_Type = type;
	}

	int EosPatch::Channel()
	{
		return m_Channel;
	}

	int EosPatch::Part()
	{
		return m_Part;
	}

	int EosPatch::Universe()
	{
		return m_Universe;
	}

	int EosPatch::Address()
	{
		return m_Address;
	}

	Platform::String^ EosPatch::Type()
	{
		return m_Type;
	}
}

