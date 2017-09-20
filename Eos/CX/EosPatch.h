#pragma once
namespace Eos
{
	public ref class EosPatch sealed
	{
	public:
		EosPatch(Platform::String^ channel, Platform::String^ part, Platform::String^ type, Platform::String^ universe, Platform::String^ address);
		int Channel();
		int Part();
		int Universe();
		int Address();
		Platform::String^ Type();

	private:
		int m_Channel;
		int m_Part;
		Platform::String^ m_Type;
		int m_Universe;
		int m_Address;
	};
}


